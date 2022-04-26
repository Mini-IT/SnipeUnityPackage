using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public partial class SnipeClient
	{
		public const int SNIPE_VERSION = 6;
		
		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;
		//public event Action UdpConnectionFailed;
		
		private bool mConnected = false;
		protected bool mLoggedIn = false;

		public bool Connected => UdpClientConnected || WebSocketConnected;
		public bool LoggedIn { get { return mLoggedIn && Connected; } }

		public string ConnectionId { get; private set; }
		public bool BadConnection { get; private set; } = false;
		
		private Stopwatch mConnectionStopwatch;
		
		private Stopwatch mServerReactionStopwatch;
		public TimeSpan CurrentRequestElapsed { get { return mServerReactionStopwatch?.Elapsed ?? new TimeSpan(0); } }
		public TimeSpan ServerReaction { get; private set; }
		
		private ArrayPool<byte> mBytesPool;

		private int mRequestId = 0;
		
		public void Connect(bool udp = true)
		{
			if (mBytesPool == null)
				mBytesPool = ArrayPool<byte>.Create();
			
			if (udp && SnipeConfig.ServerUdpPort > 0 &&
				!string.IsNullOrEmpty(SnipeConfig.ServerUdpAddress))
			{
				ConnectUdpClient();
			}
			else
			{
				ConnectWebSocket();
			}
		}
		
		
		private void OnConnected()
		{
			mConnected = true;
			
			try
			{
				ConnectionOpened?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log("[SnipeClient] ConnectionOpened invokation error: " + e.Message);
			}
		}

		private void RaiseConnectionClosedEvent()
		{
			try
			{
				ConnectionClosed?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeClient] ConnectionClosed invokation error: {e.Message}\n{e.StackTrace}");
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
			
			mConnectionStopwatch?.Stop();
			Analytics.PingTime = 0;
			
			StopSendTask();
			StopHeartbeat();
			StopCheckConnection();
			
			StopUdpNetworkLoop();
			
			if (mUdpClient != null)
			{
				mUdpClient.Disconnect();
				mUdpClient = null;
			}
			mUdpConnectionEstablished = false;

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
			
			DebugLogger.Log($"[SnipeClient] SendRequest - {message.ToJSONString()}");
			
			if (UdpClientConnected)
				DoSendRequestUdpClient(message);
			else if (WebSocketConnected)
				EnqueueMessageToSendWebSocket(message);
			
			return mRequestId;
		}

		public int SendRequest(string message_type, SnipeObject data)
		{
			if (!Connected)
				return 0;
			
			var message = new SnipeObject() { ["t"] = message_type };
			
			if (data != null)
			{
				message.Add("data", data);
			}
			
			return SendRequest(message);
		}
		
		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return;
			
			if (UdpClientConnected)
				DoSendRequestUdpClient(message);
			else if (WebSocketConnected)
				DoSendRequestWebSocket(message);
		}
		
		protected void ProcessMessage(byte[] raw_data_buffer)
		{
			PreProcessMessage();
			
			var message = MessagePackDeserializer.Parse(raw_data_buffer) as SnipeObject;
			ProcessMessage(message);
		}
		
		protected void ProcessMessage(ArraySegment<byte> raw_data_buffer)
		{
			PreProcessMessage();
			
			var message = MessagePackDeserializer.Parse(raw_data_buffer) as SnipeObject;
			ProcessMessage(message);
		}
		
		private void PreProcessMessage()
		{
			if (mServerReactionStopwatch != null)
			{
				mServerReactionStopwatch.Stop();
				ServerReaction = mServerReactionStopwatch.Elapsed;
			}
			
			StopCheckConnection();
		}
		
		private void ProcessMessage(SnipeObject message)
		{
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
						if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_LOGGED_IN)
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
	}
}