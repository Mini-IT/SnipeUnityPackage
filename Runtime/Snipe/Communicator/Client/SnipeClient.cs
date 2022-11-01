using System;
using System.Diagnostics;

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
		public event Action UdpConnectionFailed;

		private WebSocketTransport mWebSocket;
		private KcpTransport mKcp;

		protected bool mLoggedIn = false;

		public bool Connected => UdpClientConnected || WebSocketConnected;
		public bool LoggedIn { get { return mLoggedIn && Connected; } }
		public bool WebSocketConnected => mWebSocket != null && mWebSocket.Connected;
		public bool UdpClientConnected => mKcp != null && mKcp.Connected;

		public string ConnectionId { get; private set; }

		private Stopwatch mConnectionStopwatch;
		
		private Stopwatch mServerReactionStopwatch;
		public TimeSpan CurrentRequestElapsed { get { return mServerReactionStopwatch?.Elapsed ?? new TimeSpan(0); } }
		public TimeSpan ServerReaction { get; private set; }
		public double UdpConnectionTime { get; private set; }
		public double UdpDnsResolveTime => mKcp?.UdpDnsResolveTime ?? 0;
		public double UdpSocketConnectTime => mKcp?.UdpSocketConnectTime ?? 0;
		public double UdpSendHandshakeTime => mKcp?.UdpSendHandshakeTime ?? 0;

		private SnipeMessageCompressor mMessageCompressor;

		private int mRequestId = 0;
		
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
			if (mKcp != null && mKcp.Started)  // already connected or trying to connect
				return;

			if (mKcp == null)
			{
				mKcp = new KcpTransport();
				mKcp.ConnectionOpenedHandler = () =>
				{
					UdpConnectionTime = mConnectionStopwatch.Elapsed.TotalMilliseconds;
					OnConnected();
				};
				mKcp.ConnectionClosedHandler = () =>
				{
					UdpConnectionFailed?.Invoke();

					if (mKcp.ConnectionEstablished)
					{
						Disconnect(true);
					}
					else // not connected yet, try websocket
					{
						ConnectWebSocket();
					}
				};
				mKcp.MessageReceivedHandler = ProcessMessage;
			}

			mConnectionStopwatch = Stopwatch.StartNew();

			mKcp.Connect();
		}

		private void ConnectWebSocket()
		{
			if (mWebSocket != null && mWebSocket.Started)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			if (mWebSocket == null)
			{
				mWebSocket = new WebSocketTransport();
				mWebSocket.ConnectionOpenedHandler = OnConnected;
				mWebSocket.ConnectionClosedHandler = () => Disconnect(true);
				mWebSocket.MessageReceivedHandler = ProcessMessage;
			}

			mConnectionStopwatch = Stopwatch.StartNew();

			mWebSocket.Connect();
		}

		private void OnConnected()
		{
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
			
			StopResponseMonitoring();

			mKcp?.Disconnect();

			if (mWebSocket != null)
			{
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
			{
				mKcp.SendMessage(message);
			}
			else if (WebSocketConnected)
			{
				mWebSocket.SendMessage(message);
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
		
		private void ProcessMessage(SnipeObject message)
		{
			if (message == null)
				return;

			if (mServerReactionStopwatch != null)
			{
				mServerReactionStopwatch.Stop();
				ServerReaction = mServerReactionStopwatch.Elapsed;
			}

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

						mWebSocket?.SetLoggedIn(true);

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
	}
}