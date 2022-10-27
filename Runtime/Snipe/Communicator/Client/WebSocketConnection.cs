using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using MiniIT.MessagePack;
using System.Net.Sockets;
using UnityEditor.MemoryProfiler;
using System.Buffers;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class WebSocketConnection : SnipeConnection
	{
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds

		protected bool mHeartbeatEnabled = true;
		public bool HeartbeatEnabled
		{
			get { return mHeartbeatEnabled; }
			set
			{
				if (mHeartbeatEnabled != value)
				{
					mHeartbeatEnabled = value;
					if (!mHeartbeatEnabled)
						StopHeartbeat();
					else if (mLoggedIn)
						StartHeartbeat();
				}
			}
		}
		
		private Stopwatch mPingStopwatch;
		
		private WebSocketWrapper mWebSocket = null;
		private object mWebSocketLock = new object();
		
		public bool Started => mWebSocket != null;
		public bool Connected => mWebSocket != null && mWebSocket.Connected;
		
		private ConcurrentQueue<SnipeObject> mSendMessages;

		private readonly object mSendLock = new object();

		protected bool mConnected;
		protected bool mLoggedIn;

		public void Connect()
		{
			string url = SnipeConfig.GetServerUrl();

			DebugLogger.Log("[SnipeClient] WebSocket Connect to " + url);
			
			mWebSocket = new WebSocketWrapper();
			mWebSocket.OnConnectionOpened += OnWebSocketConnected;
			mWebSocket.OnConnectionClosed += OnWebSocketClosed;
			mWebSocket.ProcessMessage += ProcessWebSocketMessage;
			
			Task.Run(() => mWebSocket.Connect(url));
		}

		public void Disconnect()
		{
			mConnected = false;
			mLoggedIn = false;
			
			StopSendTask();
			StopHeartbeat();
			StopCheckConnection();

			if (mWebSocket != null)
			{
				mWebSocket.OnConnectionOpened -= OnWebSocketConnected;
				mWebSocket.OnConnectionClosed -= OnWebSocketClosed;
				mWebSocket.ProcessMessage -= ProcessWebSocketMessage;
				mWebSocket.Disconnect();
				mWebSocket = null;
			}
		}

		public void SetLoggedIn(bool value)
		{
			if (mLoggedIn == value)
				return;

			mLoggedIn = value;

			if (mLoggedIn && mHeartbeatEnabled)
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

			mLoggedIn = false;

			if (!mConnected) // failed to establish connection
			{
				SnipeConfig.NextServerUrl();
			}

			ConnectionClosedHandler?.Invoke(); // Disconnect(true);
		}
		
		public void SendMessage(SnipeObject message)
		{
			if (mSendMessages == null)
			{
				StartSendTask();
			}
			mSendMessages.Enqueue(message);
		}

		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return;

			Task.Run(() =>
			{
				lock (mSendLock)
				{
					DoSendRequestWebSocket(message);
				}
			});
		}

		private void DoSendRequestWebSocket(SnipeObject message)
		{
			string message_type = message.SafeGetString("t");
			
			lock (mWebSocketLock)
			{
				byte[] buffer = mMessageBufferProvider.GetBuffer(message_type);
				var msg_data = MessagePackSerializerNonAlloc.Serialize(ref buffer, message);

				if (SnipeConfig.CompressionEnabled && msg_data.Count >= SnipeConfig.MinMessageSizeToCompress) // compression needed
				{
					DebugLogger.Log("[SnipeClient] compress message");
					DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> compressed = mMessageCompressor.Compress(msg_data);

					DebugLogger.Log("Compressed:   " + BitConverter.ToString(compressed.Array, compressed.Offset, compressed.Count));

					mMessageBufferProvider.ReturnBuffer(message_type, buffer);

					buffer = new byte[compressed.Count + 2];
					buffer[0] = 0xAA;
					buffer[1] = 0xBB;
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, buffer, 2, compressed.Count);

					mWebSocket.SendRequest(buffer);
				}
				else // compression not needed
				{
					mWebSocket.SendRequest(msg_data);

					mMessageBufferProvider.ReturnBuffer(message_type, buffer);
				}
			}
			
			//if (mServerReactionStopwatch != null)
			//{
			//	mServerReactionStopwatch.Reset();
			//	mServerReactionStopwatch.Start();
			//}
			//else
			//{
			//	mServerReactionStopwatch = Stopwatch.StartNew();
			//}

			if (mSendMessages != null && mSendMessages.IsEmpty && !message.SafeGetString("t").StartsWith("payment/"))
			{
				StartCheckConnection();
			}
		}

		protected void ProcessWebSocketMessage(byte[] raw_data)
		{
			if (raw_data.Length < 2)
				return;

			StopCheckConnection();

			DebugLogger.Log("ProcessWebSocketMessage:   " + BitConverter.ToString(raw_data, 0, raw_data.Length));

			SnipeObject message;

			if (raw_data[0] == 0xAA && raw_data[1] == 0xBB) // compressed message
			{
				var decompressed = mMessageCompressor.Decompress(new ArraySegment<byte>(raw_data, 2, raw_data.Length - 2));

				message = MessagePackDeserializer.Parse(decompressed) as SnipeObject;
			}
			else // uncompressed
			{
				message = MessagePackDeserializer.Parse(raw_data) as SnipeObject;
			}

			MessageReceivedHandler?.Invoke(message);

			if (mHeartbeatEnabled)
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
			mSendMessages = new ConcurrentQueue<SnipeObject>();
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
			
			mSendMessages = null;
		}

		private async Task SendTask(CancellationToken? cancellation)
		{
			while (cancellation?.IsCancellationRequested != true && Connected)
			{
				if (mSendMessages != null && !mSendMessages.IsEmpty && mSendMessages.TryDequeue(out var message))
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
						lock (mWebSocketLock)
						{
							pinging = true;
							
							if (mPingStopwatch == null)
							{
								mPingStopwatch = Stopwatch.StartNew();
							}
							else
							{
								mPingStopwatch.Restart();
							}
							
							mWebSocket.Ping(pong =>
							{
								pinging = false;
								mPingStopwatch?.Stop();
								Analytics.PingTime = pong && mPingStopwatch != null ? mPingStopwatch.ElapsedMilliseconds : 0;
								
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
			if (!mLoggedIn)
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
					lock (mWebSocketLock)
					{
						pinging = true;
						mWebSocket.Ping(pong =>
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