using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	public partial class SnipeClient
	{
		public const int SNIPE_VERSION = 6;
		public const int MAX_BATCH_SIZE = 5;
		
		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;
		public event Action UdpConnectionFailed;

		private WebSocketTransport _webSocket;
		private KcpTransport _kcp;

		protected bool _loggedIn = false;

		public bool Connected => UdpClientConnected || WebSocketConnected;
		public bool LoggedIn { get { return _loggedIn && Connected; } }
		public bool WebSocketConnected => _webSocket != null && _webSocket.Connected;
		public bool UdpClientConnected => _kcp != null && _kcp.Connected;

		public string ConnectionId { get; private set; }

		private Stopwatch _connectionStopwatch;
		
		private Stopwatch _serverReactionStopwatch;
		public TimeSpan CurrentRequestElapsed { get { return _serverReactionStopwatch?.Elapsed ?? new TimeSpan(0); } }
		public TimeSpan ServerReaction { get; private set; }
		public double UdpConnectionTime { get; private set; }
		public double UdpDnsResolveTime => _kcp?.UdpDnsResolveTime ?? 0;
		public double UdpSocketConnectTime => _kcp?.UdpSocketConnectTime ?? 0;
		public double UdpSendHandshakeTime => _kcp?.UdpSendHandshakeTime ?? 0;

		private bool _batchMode = false;
		public bool BatchMode
		{
			get => _batchMode;
			set
			{
				if (value != _batchMode)
				{
					_batchMode = value;
					if (_batchMode)
					{
						_batchedRequests ??= new ConcurrentQueue<SnipeObject>();
					}
					else
					{
						FlushBatchedRequests();
						_batchedRequests = null;
					}

					DebugLogger.Log($"[SnipeClient] BatchMode = {value}");
				}
			}
		}

		private ConcurrentQueue<SnipeObject> _batchedRequests;
		private readonly object _batchLock = new object();

		private SnipeMessageCompressor _messageCompressor;

		private int mRequestId = 0;

		~SnipeClient()
		{
			_kcp?.Dispose();
			_webSocket?.Dispose();
		}
		
		public void Connect(bool udp = true)
		{
			if (_messageCompressor == null)
				_messageCompressor = new SnipeMessageCompressor();
			
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
			if (_kcp != null && _kcp.Started)  // already connected or trying to connect
				return;

			if (_kcp == null)
			{
				_kcp = new KcpTransport();
				_kcp.ConnectionOpenedHandler = () =>
				{
					UdpConnectionTime = _connectionStopwatch.Elapsed.TotalMilliseconds;
					OnConnected();
				};
				_kcp.ConnectionClosedHandler = () =>
				{
					UdpConnectionFailed?.Invoke();

					if (_kcp.ConnectionEstablished)
					{
						Disconnect(true);
					}
					else // not connected yet, try websocket
					{
						ConnectWebSocket();
					}
				};
				_kcp.MessageReceivedHandler = ProcessMessage;
			}

			_connectionStopwatch = Stopwatch.StartNew();

			_kcp.Connect();
		}

		private void ConnectWebSocket()
		{
			if (_webSocket != null && _webSocket.Started)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			if (_webSocket == null)
			{
				_webSocket = new WebSocketTransport();
				_webSocket.ConnectionOpenedHandler = OnConnected;
				_webSocket.ConnectionClosedHandler = () => Disconnect(true);
				_webSocket.MessageReceivedHandler = ProcessMessage;
			}

			_connectionStopwatch = Stopwatch.StartNew();

			_webSocket.Connect();
		}

		private void OnConnected()
		{
			_connectionStopwatch?.Stop();
			Analytics.ConnectionEstablishmentTime = _connectionStopwatch?.ElapsedMilliseconds ?? 0;

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
			_loggedIn = false;
			ConnectionId = "";
			
			_connectionStopwatch?.Stop();
			Analytics.PingTime = 0;
			
			StopResponseMonitoring();

			_kcp?.Disconnect();

			if (_webSocket != null)
			{
				_webSocket.Disconnect();
				_webSocket.Dispose();
				_webSocket = null;
			}

			if (raise_event)
			{
				RaiseConnectionClosedEvent();
			}
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

		public int SendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return 0;

			int request_id = ++mRequestId;
			message["id"] = request_id;

			if (!_loggedIn)
			{
				var data = message["data"] as SnipeObject ?? new SnipeObject();
				data["ckey"] = SnipeConfig.ClientKey;
				message["data"] = data;
			}

			if (BatchMode)
			{
				_batchedRequests.Enqueue(message);

				if (_batchedRequests.Count >= MAX_BATCH_SIZE)
				{
					FlushBatchedRequests();
				}
			}
			else
			{
				DoSendRequest(message);
			}

			return request_id;
		}

		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return;
			
			DebugLogger.Log($"[SnipeClient] SendRequest - {message.ToJSONString()}");
			
			if (UdpClientConnected)
			{
				_kcp.SendMessage(message);
			}
			else if (WebSocketConnected)
			{
				_webSocket.SendMessage(message);
			}

			if (_serverReactionStopwatch != null)
			{
				_serverReactionStopwatch.Reset();
				_serverReactionStopwatch.Start();
			}
			else
			{
				_serverReactionStopwatch = Stopwatch.StartNew();
			}

			AddResponseMonitoringItem(mRequestId, message.SafeGetString("t"));
		}

		private void DoSendBatch(List<SnipeObject> messages)
		{
			if (!Connected || messages == null || messages.Count == 0)
				return;

			DebugLogger.Log($"[SnipeClient] DoSendBatch - {messages.Count} items");

			if (UdpClientConnected)
			{
				_kcp.SendBatch(messages);
			}
			else if (WebSocketConnected)
			{
				_webSocket.SendBatch(messages);
			}
		}

		private void ProcessMessage(SnipeObject message)
		{
			if (message == null)
				return;

			if (_serverReactionStopwatch != null)
			{
				_serverReactionStopwatch.Stop();
				ServerReaction = _serverReactionStopwatch.Elapsed;
			}

			string message_type = message.SafeGetString("t");
			string error_code =  message.SafeGetString("errorCode");
			int request_id = message.SafeGetValue<int>("id");
			SnipeObject response_data = message.SafeGetValue<SnipeObject>("data");
				
			RemoveResponseMonitoringItem(request_id, message_type);
			
			DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - {request_id} - {message_type} {error_code} {response_data?.ToJSONString()}");

			if (!_loggedIn)
			{
				if (message_type == SnipeMessageTypes.USER_LOGIN)
				{	
					if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_LOGGED_IN)
					{
						DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - Login Succeeded");
							
						_loggedIn = true;

						_webSocket?.SetLoggedIn(true);

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

		private void FlushBatchedRequests()
		{
			if (_batchedRequests == null || _batchedRequests.IsEmpty)
				return;

			lock (_batchLock)
			{
				List<SnipeObject> messages = new List<SnipeObject>(MAX_BATCH_SIZE);
				while (_batchedRequests.TryDequeue(out SnipeObject message))
				{
					messages.Add(message);
				}
				DoSendBatch(messages);
			}
		}
	}
}