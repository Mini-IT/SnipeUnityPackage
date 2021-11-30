using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public class SnipeClient
	{
		public const int SNIPE_VERSION = 6;
		
		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;
		
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds

		private bool mConnected = false;
		protected bool mLoggedIn = false;

		public bool Connected { get { return (mWebSocket != null && mWebSocket.Connected); } }
		public bool LoggedIn { get { return mLoggedIn && Connected; } }

		public string ConnectionId { get; private set; }
		public bool BadConnection { get; private set; } = false;

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
		
		private Stopwatch mServerReactionStopwatch;
		public TimeSpan CurrentRequestElapsed { get { return mServerReactionStopwatch?.Elapsed ?? new TimeSpan(0); } }
		public TimeSpan ServerReaction { get; private set; }

		private int mRequestId = 0;
		private ConcurrentQueue<SnipeObject> mSendMessages;

		#region Web Socket

		private WebSocketWrapper mWebSocket = null;
		private object mWebSocketLock = new object();

		public void Connect()
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
			mWebSocket.Connect(url);
		}

		private void OnWebSocketConnected()
		{
			DebugLogger.Log("[SnipeClient] OnWebSocketConnected");
			
			mConnected = true;
			
			try
			{
				ConnectionOpened?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log("[SnipeClient] OnWebSocketConnected - ConnectionOpened invokation error: " + e.Message);
			}
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

		private void RaiseConnectionClosedEvent()
		{
			try
			{
				ConnectionClosed?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeClient] OnWebSocketClosed - ConnectionClosed invokation error: {e.Message}\n{e.StackTrace}");
			}
		}

		public void Disconnect()
		{
			Disconnect(true);
		}

		private void Disconnect(bool raise_event)
		{
			mConnected = false;
			mLoggedIn = false;
			ConnectionId = "";
			
			StopSendTask();
			StopHeartbeat();
			StopCheckConnection();

			if (mWebSocket != null)
			{
				mWebSocket.OnConnectionOpened -= OnWebSocketConnected;
				mWebSocket.OnConnectionClosed -= OnWebSocketClosed;
				mWebSocket.ProcessMessage -= ProcessMessage;
				mWebSocket.Disconnect();
				mWebSocket = null;
			}

			if (raise_event)
			{
				RaiseConnectionClosedEvent();
			}
		}

		public int SendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return 0;
				
			message["id"] = ++mRequestId;
			
			if (!mLoggedIn)
			{
				var data = message["data"] as SnipeObject ?? new SnipeObject();
				data["ckey"] = SnipeConfig.ClientKey;
				message["data"] = data;
			}
			
			if (mSendMessages == null)
			{
				StartSendTask();
			}
			mSendMessages.Enqueue(message);
			
			return mRequestId;
		}

		public int SendRequest(string message_type, SnipeObject data)
		{
			if (data == null)
			{
				return SendRequest(new SnipeObject()
				{
					["t"] = message_type,
				});
			}
			else
			{
				return SendRequest(new SnipeObject()
				{
					["t"] = message_type,
					["data"] = data
				});
			}
		}
		
		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return;
			
			DebugLogger.Log($"[SnipeClient] DoSendRequest - {message.ToJSONString()}");

			byte[] bytes = MessagePackSerializer.Serialize(message);
			lock (mWebSocketLock)
			{
				mWebSocket.SendRequest(bytes);
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

			if (mHeartbeatEnabled)
			{
				ResetHeartbeatTimer();
			}
		}
		
		protected void ProcessMessage(byte[] raw_data_buffer)
		{
			if (mServerReactionStopwatch != null)
			{
				mServerReactionStopwatch.Stop();
				ServerReaction = mServerReactionStopwatch.Elapsed;
			}
			
			StopCheckConnection();
			
			var message = MessagePackDeserializer.Parse(raw_data_buffer) as SnipeObject;

			if (message != null)
			{
				string message_type = message.SafeGetString("t");
				string error_code =  message.SafeGetString("errorCode");
				int request_id = message.SafeGetValue<int>("id");
				SnipeObject response_data = message.SafeGetValue<SnipeObject>("data");
				
				DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - {request_id} - {message_type} {error_code} {response_data?.ToJSONString()}");

				if (!mLoggedIn)
				{
					if (message_type == SnipeMessageTypes.USER_LOGIN)
					{	
						if (error_code == SnipeErrorCodes.OK || error_code == "alreadyLoggedIn")
						{
							DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - Login Succeeded");
							
							mLoggedIn = true;

							if (response_data != null)
							{
								this.ConnectionId = response_data.SafeGetString("connectionID");
							}
							else
							{
								this.ConnectionId = "";
							}

							try
							{
								LoginSucceeded?.Invoke();
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - LoginSucceeded invokation error: " + e.Message);
							}

							if (mHeartbeatEnabled)
							{
								StartHeartbeat();
							}
						}
						else
						{
							DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - Login Failed");
							
							try
							{
								LoginFailed?.Invoke(error_code);
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - LoginFailed invokation error: " + e.Message);
							}
						}
					}
				}

				if (MessageReceived != null)
				{
					try
					{
						MessageReceived.Invoke(message_type, error_code, response_data, request_id);
					}
					catch (Exception e)
					{
						DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - MessageReceived invokation error: " + e.Message + "\n" + e.StackTrace);
					}
				}
				else
				{
					DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - no MessageReceived listeners");
				}

				if (mHeartbeatEnabled)
				{
					ResetHeartbeatTimer();
				}
			}
		}

		#endregion // Web Socket

		#region Heartbeat

		private long mHeartbeatTriggerTicks = 0;

		private CancellationTokenSource mHeartbeatCancellation;

		private void StartHeartbeat()
		{
			mHeartbeatCancellation?.Cancel();

			mHeartbeatCancellation = new CancellationTokenSource();
			_ = HeartbeatTask(mHeartbeatCancellation.Token);
		}

		private void StopHeartbeat()
		{
			if (mHeartbeatCancellation != null)
			{
				mHeartbeatCancellation.Cancel();
				mHeartbeatCancellation = null;
			}
		}

		private async Task HeartbeatTask(CancellationToken cancellation)
		{
			ResetHeartbeatTimer();

			await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);

			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.UtcNow.Ticks >= mHeartbeatTriggerTicks)
				{
					lock (mWebSocketLock)
					{
						mWebSocket.Ping();
					}
					ResetHeartbeatTimer();

					DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat ping");
				}

				await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
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
		
		#region Send task

		private CancellationTokenSource mSendTaskCancellation;

		private void StartSendTask()
		{
			mSendTaskCancellation?.Cancel();
			
			mSendMessages = new ConcurrentQueue<SnipeObject>();

			mSendTaskCancellation = new CancellationTokenSource();
			_ = SendTask(mHeartbeatCancellation?.Token);
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
					
					if (mSendMessages.IsEmpty && !message.SafeGetString("t").StartsWith("payment/"))
					{
						StartCheckConnection();
					}
				}

				await Task.Yield();
			}
			
			mSendMessages = null;
		}

		#endregion
	}
}