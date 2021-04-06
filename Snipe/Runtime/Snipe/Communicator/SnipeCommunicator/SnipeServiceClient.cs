using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public class SnipeServiceClient
	{
		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;
		
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds

		protected bool mLoggedIn = false;

		public bool Connected { get { return (mWebSocket != null && mWebSocket.Connected); } }
		public bool LoggedIn { get { return mLoggedIn && Connected; } }

		public string ConnectionId { get; private set; }

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

		public void Connect()
		{
			if (mWebSocket != null)  // already connected or trying to connect
				return;

			Disconnect(); // clean up

			string url = SnipeConfig.Instance.ServiceWebsocketURL;

			DebugLogger.Log("[SnipeServiceClient] WebSocket Connect to " + url);
			
			mWebSocket = new WebSocketWrapper();
			mWebSocket.OnConnectionOpened += OnWebSocketConnected;
			mWebSocket.OnConnectionClosed += OnWebSocketClosed;
			mWebSocket.ProcessMessage += ProcessMessage;
			mWebSocket.Connect(url);
		}

		private void OnWebSocketConnected()
		{
			DebugLogger.Log("[SnipeServiceClient] OnWebSocketConnected");
			
			try
			{
				ConnectionOpened?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log("[SnipeServiceClient] OnWebSocketConnected - ConnectionOpened invokation error: " + e.Message);
			}

			RequestLogin();
		}

		protected void OnWebSocketClosed()
		{
			DebugLogger.Log("[SnipeServiceClient] OnWebSocketClosed");

			Disconnect();

			try
			{
				ConnectionClosed?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeServiceClient] OnWebSocketClosed - ConnectionClosed invokation error: {e.Message}\n{e.StackTrace}");
			}
		}

		public void Disconnect()
		{
			mLoggedIn = false;
			ConnectionId = "";
			
			StopSendTask();
			StopHeartbeat();

			if (mWebSocket != null)
			{
				mWebSocket.OnConnectionOpened -= OnWebSocketConnected;
				mWebSocket.OnConnectionClosed -= OnWebSocketClosed;
				mWebSocket.ProcessMessage -= ProcessMessage;
				mWebSocket.Disconnect();
				mWebSocket = null;
			}
		}

		public int SendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return 0;
				
			message["id"] = ++mRequestId;
			
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
			
			DebugLogger.Log($"[SnipeServiceClient] DoSendRequest - {message.ToJSONString()}");

			byte[] bytes = MessagePackSerializer.Serialize(message);
			lock (mWebSocket)
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
		
		protected void RequestLogin()
		{
			if (mLoggedIn || !Connected)
				return;

			DoSendRequest(new SnipeObject()
			{
				["t"] = SnipeMessageTypes.USER_LOGIN,
				["data"] = new SnipeObject()
				{
					["ckey"] = SnipeConfig.Instance.ClientKey,
					["id"] = SnipeAuthCommunicator.UserID,
					["token"] = SnipeAuthCommunicator.LoginToken,
					["loginGame"] = true, // Snipe V5
					["appInfo"] = SnipeConfig.Instance.AppInfo,
				}
			});
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
				
				DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - {request_id} - {message_type} {error_code} {response_data?.ToJSONString()}");

				if (!mLoggedIn)
				{
					if (message_type == SnipeMessageTypes.USER_LOGIN)
					{	
						if (error_code == SnipeErrorCodes.OK)
						{
							DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - Login Succeeded");
							
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
								DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - LoginSucceeded invokation error: " + e.Message);
							}

							if (mHeartbeatEnabled)
							{
								StartHeartbeat();
							}
						}
						else
						{
							DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - Login Failed");
							
							try
							{
								LoginFailed?.Invoke(error_code);
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - LoginFailed invokation error: " + e.Message);
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
						DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - MessageReceived invokation error: " + e.Message + "\n" + e.StackTrace);
					}
				}
				else
				{
					DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - no MessageReceived listeners");
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
					lock (mWebSocket)
					{
						mWebSocket.Ping();
					}
					ResetHeartbeatTimer();

					DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] Heartbeat ping");
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
			
			// DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] StartCheckConnection");

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

				// DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] StopCheckConnection");
			}
		}

		private async Task CheckConnectionTask(CancellationToken cancellation)
		{
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

			// Disconnect detected
			DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] CheckConnectionTask - Disconnect detected");

			OnWebSocketClosed();
		}

		#endregion
		
		#region Send task

		private CancellationTokenSource mSendTaskCancellation;

		private void StartSendTask()
		{
			mSendTaskCancellation?.Cancel();
			
			mSendMessages = new ConcurrentQueue<SnipeObject>();

			mSendTaskCancellation = new CancellationTokenSource();
			_ = SendTask(mHeartbeatCancellation.Token);
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

		private async Task SendTask(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (mSendMessages != null && !mSendMessages.IsEmpty && mSendMessages.TryDequeue(out var message))
				{
					DoSendRequest(message);
					
					if (mSendMessages.IsEmpty)
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