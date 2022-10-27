using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using kcp2k;
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

		private WebSocketConnection mWebSocketConnection;
		private KcpConnection mKcpConnection;

		//private bool mConnected = false;
		protected bool mLoggedIn = false;

		private bool mUdpConnectionEstablished;

		public bool Connected => UdpClientConnected || WebSocketConnected;
		public bool LoggedIn { get { return mLoggedIn && Connected; } }
		public bool WebSocketConnected => mWebSocketConnection != null && mWebSocketConnection.Connected;
		public bool UdpClientConnected => mKcpConnection != null && mKcpConnection.Connected;

		public string ConnectionId { get; private set; }

		public event Action UdpConnectionFailed;

		private Stopwatch mConnectionStopwatch;
		
		private Stopwatch mServerReactionStopwatch;
		public TimeSpan CurrentRequestElapsed { get { return mServerReactionStopwatch?.Elapsed ?? new TimeSpan(0); } }
		public TimeSpan ServerReaction { get; private set; }
		public double UdpConnectionTime { get; private set; }
		public double UdpDnsResolveTime => mKcpConnection?.UdpDnsResolveTime ?? 0;
		public double UdpSocketConnectTime => mKcpConnection?.UdpSocketConnectTime ?? 0;
		public double UdpSendHandshakeTime => mKcpConnection?.UdpSendHandshakeTime ?? 0;

		private SnipeMessageCompressor mMessageCompressor;

		private int mRequestId = 0;
		
		private readonly object mSendLock = new object();
		
		public void Connect(bool udp = true)
		{
			if (mMessageCompressor == null)
				mMessageCompressor = new SnipeMessageCompressor();
			
			if (udp && SnipeConfig.CheckUdpAvailable())
			{
				ConnectUdpClient();
			}
			else
			{
				ConnectWebSocket();
			}
		}

		private void ConnectUdpClient()
		{
			if (mKcpConnection != null && mKcpConnection.Started)  // already connected or trying to connect
				return;

			if (mKcpConnection == null)
			{
				mKcpConnection = new KcpConnection();
				mKcpConnection.ConnectionOpenedHandler = () =>
				{
					mUdpConnectionEstablished = true;
					UdpConnectionTime = mConnectionStopwatch.Elapsed.TotalMilliseconds;
					OnConnected();
				};
				mKcpConnection.ConnectionClosedHandler = () =>
				{
					if (mUdpConnectionEstablished)
					{
						Disconnect(true);
					}
					else // not connected yet, try websocket
					{
						ConnectWebSocket();
					}
				};
				mKcpConnection.MessageReceivedHandler = ProcessMessage;
			}

			mConnectionStopwatch = Stopwatch.StartNew();

			mUdpConnectionEstablished = false;
			mKcpConnection.Connect();
		}

		private void ConnectWebSocket()
		{
			if (mWebSocketConnection != null && mWebSocketConnection.Started)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			if (mWebSocketConnection == null)
			{
				mWebSocketConnection = new WebSocketConnection();
				mWebSocketConnection.ConnectionOpenedHandler = OnConnected;
				mWebSocketConnection.ConnectionClosedHandler = () => Disconnect(true);
				mWebSocketConnection.MessageReceivedHandler = ProcessMessage;
			}

			mConnectionStopwatch = Stopwatch.StartNew();

			mWebSocketConnection.Connect();
		}

		private void OnConnected()
		{
			//mConnected = true;

			mConnectionStopwatch?.Stop();
			Analytics.ConnectionEstablishmentTime = mConnectionStopwatch?.ElapsedMilliseconds ?? 0;

			try
			{
				ConnectionOpened?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeClient] ConnectionOpened invokation error: {e}");
				Analytics.TrackError("ConnectionOpened invokation error", e);
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
				DebugLogger.Log($"[SnipeClient] ConnectionClosed invokation error: {e}");
				Analytics.TrackError("ConnectionClosed invokation error", e);
			}
		}

		public void Disconnect()
		{
			Disconnect(true);
		}

		private void Disconnect(bool raise_event)
		{
			//mConnected = false;
			mLoggedIn = false;
			ConnectionId = "";
			
			mConnectionStopwatch?.Stop();
			Analytics.PingTime = 0;
			
			//StopSendTask();
			//StopHeartbeat();
			//StopCheckConnection();
			
			StopResponseMonitoring();

			mKcpConnection?.Disconnect();
			mUdpConnectionEstablished = false;

			if (mWebSocketConnection != null)
			{
				mWebSocketConnection.Disconnect();
				mWebSocketConnection = null;
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
			{
				mKcpConnection.SendMessage(message);
			}
			else if (WebSocketConnected)
			{
				mWebSocketConnection.SendMessage(message);
			}
			
			AddResponseMonitoringItem(mRequestId, message.SafeGetString("t"));
			
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
		
		//private void DoSendRequest(SnipeObject message)
		//{
		//	if (!Connected || message == null)
		//		return;
			
		//	if (UdpClientConnected)
		//	{
		//		Task.Run(() =>
		//		{
		//			lock(mSendLock)
		//			{
		//				DoSendRequestUdpClient(message);
		//			}
		//		});
		//	}
		//	else if (WebSocketConnected)
		//	{
		//		Task.Run(() =>
		//		{
		//			lock(mSendLock)
		//			{
		//				DoSendRequestWebSocket(message);
		//			}
		//		});
		//	}
		//}
		
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
			
			//StopCheckConnection();
		}
		
		private void ProcessMessage(SnipeObject message)
		{
			if (message == null)
				return;

			string message_type = message.SafeGetString("t");
			string error_code =  message.SafeGetString("errorCode");
			int request_id = message.SafeGetValue<int>("id");
			SnipeObject response_data = message.SafeGetValue<SnipeObject>("data");
				
			RemoveResponseMonitoringItem(request_id, message_type);
				
			DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - {request_id} - {message_type} {error_code} {response_data?.ToJSONString()}");

			if (!mLoggedIn)
			{
				if (message_type == SnipeMessageTypes.USER_LOGIN)
				{	
					if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_LOGGED_IN)
					{
						DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - Login Succeeded");
							
						mLoggedIn = true;

						mWebSocketConnection?.SetLoggedIn(true);

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
							DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - LoginSucceeded invokation error: {e}");
							Analytics.TrackError("LoginSucceeded invokation error", e);
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
							DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - LoginFailed invokation error: {e}");
							Analytics.TrackError("LoginFailed invokation error", e);
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
					DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - MessageReceived invokation error: {e}");
					Analytics.TrackError("MessageReceived invokation error", e);
				}
			}
			else
			{
				DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - no MessageReceived listeners");
			}
		}

		//[MethodImpl(MethodImplOptions.AggressiveInlining)]
		//private bool TryReturnMessageBuffer(byte[] buffer)
		//{
		//	// if buffer.Length > mBytesPool's max bucket size (1024*1024 = 1048576)
		//	// then the buffer can not be returned to the pool. It will be dropped.
		//	// And ArgumentException will be thown.
		//	try
		//	{
		//		mBytesPool.Return(buffer);
		//	}
		//	catch (ArgumentException)
		//	{
		//		// ignore
		//		return false;
		//	}

		//	return true;
		//}
	}
}