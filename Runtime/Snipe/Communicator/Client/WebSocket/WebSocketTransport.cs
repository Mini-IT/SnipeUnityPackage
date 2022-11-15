using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public class WebSocketTransport : Transport
	{
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds

		protected bool _heartbeatEnabled = true;
		public bool HeartbeatEnabled
		{
			get { return _heartbeatEnabled; }
			set
			{
				if (_heartbeatEnabled != value)
				{
					_heartbeatEnabled = value;
					if (!_heartbeatEnabled)
						StopHeartbeat();
					else if (_loggedIn)
						StartHeartbeat();
				}
			}
		}
		
		private Stopwatch _pingStopwatch;
		
		private WebSocketWrapper _webSocket = null;
		private readonly object _lock = new object();
		
		public bool Started => _webSocket != null;
		public bool Connected => _webSocket != null && _webSocket.Connected;
		
		private ConcurrentQueue<SnipeObject> _sendMessages;

		protected bool _connected;
		protected bool _loggedIn;

		public void Connect()
		{
            Task.Run((Action)(() =>
			{
				lock (this._lock)
				{
                    ConnectTask();
				}
			}));
		}

		private void ConnectTask()
		{
			string url = SnipeConfig.GetWebSocketUrl();

			DebugLogger.Log("[SnipeClient] WebSocket Connect to " + url);
			
			_webSocket = new WebSocketWrapper();
			_webSocket.OnConnectionOpened += OnWebSocketConnected;
			_webSocket.OnConnectionClosed += OnWebSocketClosed;
			_webSocket.ProcessMessage += ProcessWebSocketMessage;
			
			_webSocket.Connect(url);
		}

		public void Disconnect()
		{
			_connected = false;
			_loggedIn = false;

			lock (_lock)
			{
				StopSendTask();
				StopHeartbeat();
				StopCheckConnection();

				if (_webSocket != null)
				{
					_webSocket.OnConnectionOpened -= OnWebSocketConnected;
					_webSocket.OnConnectionClosed -= OnWebSocketClosed;
					_webSocket.ProcessMessage -= ProcessWebSocketMessage;
					_webSocket.Disconnect();
					_webSocket = null;
				}
			}
		}

		public void SetLoggedIn(bool value)
		{
			if (_loggedIn == value)
				return;

			_loggedIn = value;

			if (_loggedIn && _heartbeatEnabled)
			{
				StartHeartbeat();
			}
		}

		private void OnWebSocketConnected()
		{
			DebugLogger.Log($"[SnipeClient] OnWebSocketConnected");

			_mainThreadActions.Enqueue(() =>
			{
				ConnectionOpenedHandler?.Invoke();
			});
		}
		
		protected void OnWebSocketClosed()
		{
			DebugLogger.Log("[SnipeClient] OnWebSocketClosed");

			_loggedIn = false;

			if (!_connected) // failed to establish connection
			{
				SnipeConfig.NextWebSocketUrl();
			}

			_mainThreadActions.Enqueue(() =>
			{
				ConnectionClosedHandler?.Invoke();
			});
		}
		
		public void SendMessage(SnipeObject message)
		{
			if (_sendMessages == null)
			{
				StartSendTask();
			}
			_sendMessages.Enqueue(message);
		}

		private async void DoSendRequest(SnipeObject message)
		{
			byte[] data = await SerializeMessage(message);

			lock (_lock)
			{
				_webSocket.SendRequest(data);
			}

			if (_sendMessages != null && _sendMessages.IsEmpty && !message.SafeGetString("t").StartsWith("payment/"))
			{
				StartCheckConnection();
			}
		}

		private async Task<byte[]> SerializeMessage(SnipeObject message)
		{
			string message_type = message.SafeGetString("t");

			byte[] result = null;
			byte[] buffer = _messageBufferProvider.GetBuffer(message_type);
			var msg_data = await Task.Run(() => MessagePackSerializerNonAlloc.Serialize(ref buffer, message));

			if (SnipeConfig.CompressionEnabled && msg_data.Count >= SnipeConfig.MinMessageSizeToCompress) // compression needed
			{
				await Task.Run(() =>
				{
					DebugLogger.Log("[SnipeClient] compress message");
					//DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> compressed = _messageCompressor.Compress(msg_data);

					//DebugLogger.Log("Compressed:   " + BitConverter.ToString(compressed.Array, compressed.Offset, compressed.Count));

					_messageBufferProvider.ReturnBuffer(message_type, buffer);

					result = new byte[compressed.Count + 2];
					result[0] = 0xAA;
					result[1] = 0xBB;
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, result, 2, compressed.Count);
				});
			}
			else // compression not needed
			{
				_messageBufferProvider.ReturnBuffer(message_type, buffer);

				result = new byte[msg_data.Count];
				Array.ConstrainedCopy(msg_data.Array, msg_data.Offset, result, 0, msg_data.Count);
			}

			return result;
		}

		protected void ProcessWebSocketMessage(byte[] raw_data)
		{
			if (raw_data.Length < 2)
				return;

			StopCheckConnection();

			ProcessMessage(raw_data);
		}

		private async void ProcessMessage(byte[] raw_data)
		{
			DebugLogger.Log("ProcessWebSocketMessage"); //   " + BitConverter.ToString(raw_data, 0, raw_data.Length));

			SnipeObject message;

			if (raw_data[0] == 0xAA && raw_data[1] == 0xBB) // compressed message
			{
				message = await Task.Run(() =>
				{
					var decompressed = _messageCompressor.Decompress(new ArraySegment<byte>(raw_data, 2, raw_data.Length - 2));
					return MessagePackDeserializer.Parse(decompressed) as SnipeObject;
				});
			}
			else // uncompressed
			{
				message = await Task.Run(() =>
				{
					return MessagePackDeserializer.Parse(raw_data) as SnipeObject;
				});
			}

			_mainThreadActions.Enqueue(() =>
			{
				MessageReceivedHandler?.Invoke(message);
			});

			if (_heartbeatEnabled)
			{
				ResetHeartbeatTimer();
			}
		}

		#region Send task

		private CancellationTokenSource mSendTaskCancellation;

		private void StartSendTask()
		{
			mSendTaskCancellation?.Cancel();
			
#if NET5_0_OR_GREATER
			if (mSendMessages == null)
				mSendMessages = new ConcurrentQueue<SnipeObject>();
			else
				mSendMessages.Clear();
#else
			_sendMessages = new ConcurrentQueue<SnipeObject>();
#endif

			mSendTaskCancellation = new CancellationTokenSource();
			
			Task.Run(() =>
			{
				try
				{
					SendTask(mSendTaskCancellation?.Token).GetAwaiter().GetResult();
				}
				catch (Exception task_exception)
				{
					var e = task_exception is AggregateException ae ? ae.InnerException : task_exception;
					DebugLogger.Log($"[SnipeClient] [] SendTask Exception: {e}");
					Analytics.TrackError("WebSocket SendTask error", e);
					
					StopSendTask();
				}
			});
		}

		private void StopSendTask()
		{
			StopCheckConnection();
			
			if (mSendTaskCancellation != null)
			{
				mSendTaskCancellation.Cancel();
				mSendTaskCancellation = null;
			}
			
			_sendMessages = null;
		}

		private async Task SendTask(CancellationToken? cancellation)
		{
			while (cancellation?.IsCancellationRequested != true && Connected)
			{
				if (_sendMessages != null && !_sendMessages.IsEmpty && _sendMessages.TryDequeue(out var message) && message != null)
				{
					DoSendRequest(message);
				}

				await Task.Delay(100);
			}
		}

		#endregion // Send task

		#region Heartbeat

		private long mHeartbeatTriggerTicks = 0;

		private CancellationTokenSource mHeartbeatCancellation;

		private void StartHeartbeat()
		{
			mHeartbeatCancellation?.Cancel();

			mHeartbeatCancellation = new CancellationTokenSource();
			Task.Run(() => HeartbeatTask(mHeartbeatCancellation.Token));
		}

		private void StopHeartbeat()
		{
			if (mHeartbeatCancellation != null)
			{
				mHeartbeatCancellation.Cancel();
				mHeartbeatCancellation = null;
			}
		}

		private async void HeartbeatTask(CancellationToken cancellation)
		{
			//ResetHeartbeatTimer();

			// await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
			mHeartbeatTriggerTicks = 0;

			while (cancellation != null && !cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.UtcNow.Ticks >= mHeartbeatTriggerTicks)
				{
					bool pinging = false;
					if (pinging)
					{
						await Task.Delay(20);
					}
					else
					{
						lock (_lock)
						{
							pinging = true;
							
							if (_pingStopwatch == null)
							{
								_pingStopwatch = Stopwatch.StartNew();
							}
							else
							{
								_pingStopwatch.Restart();
							}
							
							_webSocket.Ping(pong =>
							{
								pinging = false;
								_pingStopwatch?.Stop();
								Analytics.PingTime = pong && _pingStopwatch != null ? _pingStopwatch.ElapsedMilliseconds : 0;
								
								if (pong)
									DebugLogger.Log($"[SnipeClient] [] Heartbeat pong {Analytics.PingTime} ms");
								else
									DebugLogger.Log($"[SnipeClient] [] Heartbeat pong NOT RECEIVED");
							});
						}
					}
					
					ResetHeartbeatTimer();

					DebugLogger.Log($"[SnipeClient] [] Heartbeat ping");
				}
				
				if (cancellation == null || cancellation.IsCancellationRequested)
				{
					break;
				}
				
				try
				{
					await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}

		private void ResetHeartbeatTimer()
		{
			mHeartbeatTriggerTicks = DateTime.UtcNow.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		#endregion

		#region CheckConnection

		public bool BadConnection { get; private set; } = false;

		private CancellationTokenSource mCheckConnectionCancellation;
		
		private void StartCheckConnection()
		{
			if (!_loggedIn)
				return;
			
			// DebugLogger.Log($"[SnipeClient] [] StartCheckConnection");

			mCheckConnectionCancellation?.Cancel();

			mCheckConnectionCancellation = new CancellationTokenSource();
			Task.Run(() => CheckConnectionTask(mCheckConnectionCancellation.Token));
		}

		private void StopCheckConnection()
		{
			if (mCheckConnectionCancellation != null)
			{
				mCheckConnectionCancellation.Cancel();
				mCheckConnectionCancellation = null;

				// DebugLogger.Log($"[SnipeClient] [] StopCheckConnection");
			}
			
			BadConnection = false;
		}

		private async void CheckConnectionTask(CancellationToken cancellation)
		{
			BadConnection = false;
			
			try
			{
				await Task.Delay(CHECK_CONNECTION_TIMEOUT, cancellation);
			}
			catch (TaskCanceledException)
			{
				// This is OK. Just terminating the task
				return;
			}

			// if the connection is ok then this task should already be cancelled
			if (cancellation == null || cancellation.IsCancellationRequested)
				return;
			
			BadConnection = true;
			DebugLogger.Log($"[SnipeClient] [] CheckConnectionTask - Bad connection detected");
			
			bool pinging = false;
			while (Connected && BadConnection)
			{
				// if the connection is ok then this task should be cancelled
				if (cancellation == null || cancellation.IsCancellationRequested)
				{
					BadConnection = false;
					return;
				}
				
				if (pinging)
				{
					await Task.Delay(100);
				}
				else
				{
					lock (_lock)
					{
						pinging = true;
						_webSocket.Ping(pong =>
						{
							pinging = false;
							
							if (pong)
							{
								BadConnection = false;
								DebugLogger.Log($"[SnipeClient] [] CheckConnectionTask - pong received");
							}
							else
							{
								DebugLogger.Log($"[SnipeClient] [] CheckConnectionTask - pong NOT received");
								OnDisconnectDetected();
							}
						});
					}
				}
			}
		}
		
		private void OnDisconnectDetected()
		{
			if (Connected)
			{
				// Disconnect detected
				DebugLogger.Log($"[SnipeClient] [] CheckConnectionTask - Disconnect detected");

				OnWebSocketClosed();
			}
		}

		#endregion
	}
}