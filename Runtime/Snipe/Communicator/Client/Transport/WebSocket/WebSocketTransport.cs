using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public class WebSocketTransport : Transport
	{
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds
		private readonly byte[] COMPRESSED_HEADER = new byte[] { 0xAA, 0xBB };
		private readonly byte[] BATCH_HEADER = new byte[] { 0xAA, 0xBC };

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
		
		public override bool Started => _webSocket != null;
		public override bool Connected => _webSocket != null && _webSocket.Connected;

		private Stopwatch _pingStopwatch;

		private WebSocketWrapper _webSocket = null;
		private readonly object _lock = new object();
		
		private ConcurrentQueue<SnipeObject> _sendMessages;
		private ConcurrentQueue<List<SnipeObject>> _batchMessages;

		private readonly SnipeConfig _config;
		private readonly Analytics _analytics;

		private bool _connected;
		private bool _loggedIn;

		internal WebSocketTransport(SnipeConfig config)
		{
			_config = config;
			_analytics = Analytics.GetInstance(config.ContextId);
		}

		public override void Connect()
		{
			string url = _config.GetWebSocketUrl();

			DebugLogger.Log("[SnipeClient] WebSocket Connect to " + url);

			if (_config.WebSocketImplementation == SnipeConfig.WebSocketImplementations.ClientWebSocket)
				_webSocket = new WebSocketClientWrapper();
			else
				_webSocket = new WebSocketSharpWrapper();

			_webSocket.OnConnectionOpened += OnWebSocketConnected;
			_webSocket.OnConnectionClosed += OnWebSocketClosed;
			_webSocket.ProcessMessage += ProcessWebSocketMessage;

			Task.Run(() =>
			{
				_analytics.ConnectionUrl = url;
				_webSocket.Connect(url);
			});
		}

		public override void Disconnect()
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

			ConnectionOpenedHandler?.Invoke();
		}
		
		protected void OnWebSocketClosed()
		{
			DebugLogger.Log("[SnipeClient] OnWebSocketClosed");

			_loggedIn = false;

			if (!_connected) // failed to establish connection
			{
				_config.NextWebSocketUrl();
			}

			ConnectionClosedHandler?.Invoke();
		}
		
		public override void SendMessage(SnipeObject message)
		{
			if (_sendMessages == null)
			{
				StartSendTask();
			}
			_sendMessages.Enqueue(message);
		}

		public override void SendBatch(List<SnipeObject> messages)
		{
			lock (_lock)
			{
				if (_batchMessages == null)
					_batchMessages = new ConcurrentQueue<List<SnipeObject>>();
				_batchMessages.Enqueue(messages);

				if (_sendMessages == null)
				{
					StartSendTask();
				}
			}
		}

		private async void DoSendRequest(SnipeObject message)
		{
			byte[] data = await SerializeMessage(message);

			lock (_lock)
			{
				_webSocket?.SendRequest(data);
			}

			if (_sendMessages != null && _sendMessages.IsEmpty && !message.SafeGetString("t").StartsWith("payment/"))
			{
				StartCheckConnection();
			}
		}

		private async void DoSendBatch(List<SnipeObject> messages)
		{
			byte[][] data = new byte[messages.Count][];
			int length = 2; // 2 bytes for batch header
			for (int i = 0; i < messages.Count; i++)
			{
				data[i] = await SerializeMessage(messages[i]);
				length += data[i].Length + 3; // 3 bytes for serialized message size
			}

			byte[] request = new byte[length]; // don't use ArrayPool<byte> here
			request[0] = BATCH_HEADER[0];
			request[1] = BATCH_HEADER[1];
			int offset = 2;
			for (int i = 0; i < data.Length; i++)
			{
				length = data[i].Length;
				BytesUtil.WriteInt3(request, offset, length);
				offset += 3;
				Array.ConstrainedCopy(data[i], 0, request, offset, length);
				offset += length;
			}

			_webSocket?.SendRequest(request);
		}

		// [Testable]
		internal async Task<byte[]> SerializeMessage(SnipeObject message)
		{
			byte[] result = null;

			try
			{
				await _messageSerializationSemaphore.WaitAsync();

				var msg_data = await Task.Run(() => _messageSerializer.Serialize(ref _messageSerializationBuffer, message));

				if (_config.CompressionEnabled && msg_data.Count >= _config.MinMessageBytesToCompress) // compression needed
				{
					await Task.Run(() =>
					{
						DebugLogger.Log("[SnipeClient] compress message");
						//DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

						ArraySegment<byte> compressed = _messageCompressor.Compress(msg_data);

						//DebugLogger.Log("Compressed:   " + BitConverter.ToString(compressed.Array, compressed.Offset, compressed.Count));

						result = new byte[compressed.Count + 2];
						result[0] = COMPRESSED_HEADER[0];
						result[1] = COMPRESSED_HEADER[1];
						Array.ConstrainedCopy(compressed.Array, compressed.Offset, result, 2, compressed.Count);
					});
				}
				else // compression not needed
				{
					result = new byte[msg_data.Count];
					Array.ConstrainedCopy(msg_data.Array, msg_data.Offset, result, 0, msg_data.Count);
				}
			}
			finally
			{
				_messageSerializationSemaphore.Release();
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

			try
			{
				await _messageProcessingSemaphore.WaitAsync();

				message = await Task.Run(() => ReadMessage(raw_data));
			}
			finally
			{
				_messageProcessingSemaphore.Release();
			}

			MessageReceivedHandler?.Invoke(message);

			if (_heartbeatEnabled)
			{
				ResetHeartbeatTimer();
			}
		}

		private SnipeObject ReadMessage(byte[] buffer)
		{
			bool compressed = (buffer[0] == COMPRESSED_HEADER[0] && buffer[1] == COMPRESSED_HEADER[1]);
			if (compressed)
			{
				var decompressed = _messageCompressor.Decompress(new ArraySegment<byte>(buffer, 2, buffer.Length - 2));
				return MessagePackDeserializer.Parse(decompressed) as SnipeObject;
			}
			else // uncompressed
			{
				return MessagePackDeserializer.Parse(buffer) as SnipeObject;
			}
		}

		#region Send task

		private CancellationTokenSource _sendTaskCancellation;

		private void StartSendTask()
		{
			_sendTaskCancellation?.Cancel();

#if NET5_0_OR_GREATER
			if (_sendMessages == null)
				_sendMessages = new ConcurrentQueue<SnipeObject>();
			else
				_sendMessages.Clear();
#else
			_sendMessages = new ConcurrentQueue<SnipeObject>();
#endif

			_sendTaskCancellation = new CancellationTokenSource();
			
			Task.Run(() =>
			{
				try
				{
					SendTask(_sendTaskCancellation?.Token).GetAwaiter().GetResult();
				}
				catch (Exception task_exception)
				{
					var e = task_exception is AggregateException ae ? ae.InnerException : task_exception;
					DebugLogger.Log($"[SnipeClient] [] SendTask Exception: {e}");
					_analytics.TrackError("WebSocket SendTask error", e);
					
					StopSendTask();
				}
			});
		}

		private void StopSendTask()
		{
			StopCheckConnection();
			
			if (_sendTaskCancellation != null)
			{
				_sendTaskCancellation.Cancel();
				_sendTaskCancellation = null;
			}
			
			_sendMessages = null;
		}

		private async Task SendTask(CancellationToken? cancellation)
		{
			while (cancellation?.IsCancellationRequested != true && Connected)
			{
				if (_batchMessages != null && !_batchMessages.IsEmpty && _batchMessages.TryDequeue(out var messages) && messages != null && messages.Count > 0)
				{
					DoSendBatch(messages);
				}

				if (_sendMessages != null && !_sendMessages.IsEmpty && _sendMessages.TryDequeue(out var message) && message != null)
				{
					DoSendRequest(message);
				}

				await Task.Delay(100);
			}
		}

		#endregion // Send task

		#region Heartbeat

		private long _heartbeatTriggerTicks = 0;

		private CancellationTokenSource _heartbeatCancellation;

		private void StartHeartbeat()
		{
			_heartbeatCancellation?.Cancel();

			// Custom heartbeating is needed only for WebSocketSharp
			if (_webSocket == null && _config.WebSocketImplementation != SnipeConfig.WebSocketImplementations.WebSocketSharp ||
				_webSocket != null && !(_webSocket is WebSocketSharpWrapper))
			{
				_heartbeatEnabled = false;
				return;
			}

			_heartbeatCancellation = new CancellationTokenSource();
			Task.Run(() => HeartbeatTask(_heartbeatCancellation.Token));
		}

		private void StopHeartbeat()
		{
			if (_heartbeatCancellation != null)
			{
				_heartbeatCancellation.Cancel();
				_heartbeatCancellation = null;
			}
		}

		private async void HeartbeatTask(CancellationToken cancellation)
		{
			//ResetHeartbeatTimer();

			// await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
			_heartbeatTriggerTicks = 0;

			while (cancellation != null && !cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.UtcNow.Ticks >= _heartbeatTriggerTicks)
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
								_analytics.PingTime = pong && _pingStopwatch != null ? _pingStopwatch.Elapsed : TimeSpan.Zero;
								
								if (pong)
									DebugLogger.Log($"[SnipeClient] [] Heartbeat pong {_analytics.PingTime.TotalMilliseconds} ms");
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
			_heartbeatTriggerTicks = DateTime.UtcNow.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		#endregion

		#region CheckConnection

		public bool BadConnection { get; private set; } = false;

		private CancellationTokenSource _checkConnectionCancellation;

		private void StartCheckConnection()
		{
			if (!_loggedIn)
				return;
			
			// DebugLogger.Log($"[SnipeClient] [] StartCheckConnection");

			_checkConnectionCancellation?.Cancel();

			_checkConnectionCancellation = new CancellationTokenSource();
			Task.Run(() => CheckConnectionTask(_checkConnectionCancellation.Token));
		}

		private void StopCheckConnection()
		{
			if (_checkConnectionCancellation != null)
			{
				_checkConnectionCancellation.Cancel();
				_checkConnectionCancellation = null;

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
