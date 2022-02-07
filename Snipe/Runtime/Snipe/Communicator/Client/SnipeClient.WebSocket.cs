using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public partial class SnipeClient
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
					else if (LoggedIn)
						StartHeartbeat();
				}
			}
		}
		
		private Stopwatch mPingStopwatch;
		
		#region Web Socket

		private WebSocketWrapper mWebSocket = null;
		private object mWebSocketLock = new object();
		
		public bool WebSocketConnected => mWebSocket != null && mWebSocket.Connected;
		
		private void ConnectWebSocket()
		{
			if (mWebSocket != null)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			string url = SnipeConfig.GetServerUrl();

			DebugLogger.Log("[SnipeClient] WebSocket Connect to " + url);
			
			mWebSocket = new WebSocketWrapper();
			mWebSocket.OnConnectionOpened += OnWebSocketConnected;
			mWebSocket.OnConnectionClosed += OnWebSocketClosed;
			mWebSocket.ProcessMessage += ProcessMessage;
			
			mConnectionStopwatch = Stopwatch.StartNew();
			
			mWebSocket.Connect(url);
		}

		private void OnWebSocketConnected()
		{
			DebugLogger.Log($"[SnipeClient] OnWebSocketConnected");
			
			mConnectionStopwatch?.Stop();
			Analytics.ConnectionEstablishmentTime = mConnectionStopwatch?.ElapsedMilliseconds ?? 0;
			
			OnConnected();
		}
		
		protected void OnWebSocketClosed()
		{
			DebugLogger.Log("[SnipeClient] OnWebSocketClosed");
			
			if (!mConnected) // failed to establish connection
			{
				SnipeConfig.NextServerUrl();
			}

			Disconnect(true);
		}
		
		private void DoSendRequestWebSocket(SnipeObject message)
		{
			lock (mWebSocketLock)
			{
				byte[] buffer = mBytesPool.Rent(MessagePackSerializerNonAlloc.MaxUsedBufferSize);
				var msg_data = MessagePackSerializerNonAlloc.Serialize(ref buffer, message);
				var data = new byte[msg_data.Count];
				Array.ConstrainedCopy(msg_data.Array, msg_data.Offset, data, 0, msg_data.Count);
				mWebSocket.SendRequest(data);
				mBytesPool.Return(buffer);
			}
			
			if (mServerReactionStopwatch != null)
			{
				mServerReactionStopwatch.Reset();
				mServerReactionStopwatch.Start();
			}
			else
			{
				mServerReactionStopwatch = Stopwatch.StartNew();
			}

			// if (mHeartbeatEnabled)
			// {
				// ResetHeartbeatTimer();
			// }
		}
		
		#endregion // Web Socket
		
		

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

			while (cancellation != null && !cancellation.IsCancellationRequested && WebSocketConnected)
			{
				if (DateTime.UtcNow.Ticks >= mHeartbeatTriggerTicks)
				{
					bool pinging = false;
					if (pinging)
					{
						await Task.Yield();
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
									DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat pong {Analytics.PingTime} ms");
								else
									DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat pong NOT RECEIVED");
							});
						}
					}
					
					ResetHeartbeatTimer();

					DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat ping");
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

		private CancellationTokenSource mCheckConnectionCancellation;
		
		private void StartCheckConnection()
		{
			if (!mLoggedIn)
				return;
			
			// DebugLogger.Log($"[SnipeClient] [{ConnectionId}] StartCheckConnection");

			mCheckConnectionCancellation?.Cancel();

			mCheckConnectionCancellation = new CancellationTokenSource();
			_ = CheckConnectionTask(mCheckConnectionCancellation.Token);
		}

		private void StopCheckConnection()
		{
			if (mCheckConnectionCancellation != null)
			{
				mCheckConnectionCancellation.Cancel();
				mCheckConnectionCancellation = null;

				// DebugLogger.Log($"[SnipeClient] [{ConnectionId}] StopCheckConnection");
			}
			
			BadConnection = false;
		}

		private async Task CheckConnectionTask(CancellationToken cancellation)
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
			if (cancellation.IsCancellationRequested)
				return;
			
			BadConnection = true;
			DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - Bad connection detected");
			
			bool pinging = false;
			while (Connected)
			{
				if (pinging)
				{
					await Task.Yield();
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
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - pong received");
							}
							else
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - pong NOT received");
								OnDisconnectDetected();
							}
						});
					}
				}
				
				// if the connection is ok then this task should already be cancelled
				if (cancellation.IsCancellationRequested)
				{
					BadConnection = false;
					return;
				}
			}
			
			// try
			// {
				// await Task.Delay(CHECK_CONNECTION_TIMEOUT * 2, cancellation);
			// }
			// catch (TaskCanceledException)
			// {
				// // This is OK. Just terminating the task
				// BadConnection = false;
				// return;
			// }
			
			// // if the connection is ok then this task should already be cancelled
			// if (cancellation.IsCancellationRequested)
			// {
				// BadConnection = false;
				// return;
			// }
			
			// OnDisconnectDetected();
		}
		
		private void OnDisconnectDetected()
		{
			if (Connected)
			{
				// Disconnect detected
				DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - Disconnect detected");

				OnWebSocketClosed();
			}
		}

		#endregion
	}
}