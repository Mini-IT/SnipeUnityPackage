using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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

		private Transport _transport;

		protected bool _loggedIn = false;

		public bool Connected => _transport != null && _transport.Connected;
		public bool LoggedIn { get { return _loggedIn && Connected; } }
		public bool WebSocketConnected => Connected && _transport is WebSocketTransport;
		public bool UdpClientConnected => Connected && _transport is KcpTransport;
		public bool HttpClientConnected => Connected && _transport is HttpTransport;

		public string ConnectionId { get; private set; }

		private Stopwatch _connectionStopwatch;
		
		private Stopwatch _serverReactionStopwatch;
		public TimeSpan CurrentRequestElapsed => _serverReactionStopwatch?.Elapsed ?? new TimeSpan(0);
		
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

		private int _requestId = 0;

		private TaskScheduler _mainThreadScheduler;
		private readonly SnipeConfig _config;
		private readonly Analytics _analytics;

		internal SnipeClient(SnipeConfig config)
		{
			_config = config;
			_analytics = Analytics.GetInstance(config.ContextId);
		}

		public void Connect()
		{
			_mainThreadScheduler = SynchronizationContext.Current != null ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;

			//if (_config.CheckUdpAvailable())
			//{
			//	StartTransport(CreateKcpTransport);
			//}
			//else
			//{
			//	StartTransport(CreateWebSocketTransport);
			//}
			StartTransport(CreateHttpTransport);
		}

		private void RunInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}

		private void StartTransport(Func<Transport> transportFabric)
		{
			if (_transport != null && _transport.Started)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			_transport ??= transportFabric.Invoke();

			_connectionStopwatch = Stopwatch.StartNew();
			_transport.Connect();
		}

		#region Transport fabrics

		private KcpTransport CreateKcpTransport()
		{
			var transport = new KcpTransport(_config, _analytics);

			transport.ConnectionOpenedHandler = () =>
			{
				_analytics.UdpConnectionTime = _connectionStopwatch.Elapsed;
				OnConnected();
			};

			transport.ConnectionClosedHandler = () =>
			{
				RunInMainThread(() =>
				{
					UdpConnectionFailed?.Invoke();
				});

				if (transport.ConnectionEstablished)
				{
					Disconnect(true);
				}
				else // not connected yet, try websocket
				{
					StartTransport(CreateWebSocketTransport);
				}
			};

			transport.MessageReceivedHandler = ProcessMessage;

			return transport;
		}

		private WebSocketTransport CreateWebSocketTransport()
		{
			return new WebSocketTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = OnConnected,
				ConnectionClosedHandler = () => Disconnect(true),
				MessageReceivedHandler = ProcessMessage
			};
		}

		private HttpTransport CreateHttpTransport()
		{
			return new HttpTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = OnConnected,
				ConnectionClosedHandler = () => Disconnect(true),
				MessageReceivedHandler = ProcessMessage,
			};
		}

		#endregion

		private void OnConnected()
		{
			_connectionStopwatch?.Stop();
			_analytics.ConnectionEstablishmentTime = _connectionStopwatch?.Elapsed ?? TimeSpan.Zero;

			RunInMainThread(() =>
			{
				try
				{
					ConnectionOpened?.Invoke();
				}
				catch (Exception e)
				{
					DebugLogger.LogFormat("[SnipeClient] ConnectionOpened invocation error: {0}", e);
					_analytics.TrackError("ConnectionOpened invocation error", e);
				}
			});
		}

		private void RaiseConnectionClosedEvent()
		{
			RunInMainThread(() =>
			{
				try
				{
					ConnectionClosed?.Invoke();
				}
				catch (Exception e)
				{
					DebugLogger.LogFormat("[SnipeClient] ConnectionClosed invocation error: {0}", e);
					_analytics.TrackError("ConnectionClosed invocation error", e);
				}
			});
		}

		public void Disconnect()
		{
			Disconnect(true);
		}

		private void Disconnect(bool raiseEvent)
		{
			_loggedIn = false;
			ConnectionId = "";
			
			_connectionStopwatch?.Stop();
			_analytics.PingTime = TimeSpan.Zero;
			_analytics.ServerReaction = TimeSpan.Zero;

			StopResponseMonitoring();

			if (_transport != null)
			{
				_transport.Disconnect();
				_transport = null;
			}

			if (raiseEvent)
			{
				RaiseConnectionClosedEvent();
			}
		}

		public int SendRequest(string messageType, SnipeObject data)
		{
			if (!Connected)
			{
				return 0;
			}

			var message = new SnipeObject() { ["t"] = messageType };

			if (data != null)
			{
				message.Add("data", data);
			}

			return SendRequest(message);
		}

		public int SendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
			{
				return 0;
			}

			int requestId = ++_requestId;
			message["id"] = requestId;

			SnipeObject data = null;

			if (!_loggedIn && (_batchedRequests == null || _batchedRequests.IsEmpty))
			{
				data = message["data"] as SnipeObject ?? new SnipeObject();
				data["ckey"] = _config.ClientKey;
				message["data"] = data;
			}

			if (_config.DebugId != null)
			{
				data ??= message["data"] as SnipeObject ?? new SnipeObject();
				data["debugID"] = _config.DebugId;
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

			return requestId;
		}

		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
			{
				return;
			}
			
			if (DebugLogger.IsEnabled)
			{
				DebugLogger.LogFormat("[SnipeClient] SendRequest - {0}", message.ToJSONString());
			}
			
			_transport.SendMessage(message);

			if (_serverReactionStopwatch != null)
			{
				_serverReactionStopwatch.Reset();
				_serverReactionStopwatch.Start();
			}
			else
			{
				_serverReactionStopwatch = Stopwatch.StartNew();
			}

			AddResponseMonitoringItem(_requestId, message.SafeGetString("t"));
		}

		private void DoSendBatch(List<SnipeObject> messages)
		{
			if (!Connected || messages == null || messages.Count == 0)
				return;

			DebugLogger.LogFormat("[SnipeClient] DoSendBatch - {0} items", messages.Count);

			_transport.SendBatch(messages);
		}

		private void ProcessMessage(SnipeObject message)
		{
			if (message == null)
			{
				return;
			}

			if (_serverReactionStopwatch != null)
			{
				_serverReactionStopwatch.Stop();
				_analytics.ServerReaction = _serverReactionStopwatch.Elapsed;
			}

			string messageType = message.SafeGetString("t");
			string errorCode = message.SafeGetString("errorCode");
			int requestId = message.SafeGetValue<int>("id");
			SnipeObject responseData = message.SafeGetValue<SnipeObject>("data");

			RemoveResponseMonitoringItem(requestId, messageType);

			if (DebugLogger.IsEnabled)
			{
				DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - {1} - {2} {3} {4}", ConnectionId, requestId, messageType, errorCode, responseData?.ToFastJSONString());
			}

			if (!_loggedIn && messageType == SnipeMessageTypes.USER_LOGIN)
			{
				ProcessLoginResponse(errorCode, responseData);
			}

			InvokeMessageReceived(messageType, errorCode, requestId, responseData);
		}

		private void ProcessLoginResponse(string errorCode, SnipeObject responseData)
		{
			if (errorCode == SnipeErrorCodes.OK || errorCode == SnipeErrorCodes.ALREADY_LOGGED_IN)
			{
				DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - Login Succeeded", ConnectionId);

				_loggedIn = true;

				if (_transport is WebSocketTransport webSocketTransport)
				{
					webSocketTransport.SetLoggedIn(true);
				}

				if (responseData != null)
				{
					ConnectionId = responseData.SafeGetString("connectionID");
				}
				else
				{
					ConnectionId = "";
				}

				if (LoginSucceeded != null)
				{
					RunInMainThread(() =>
					{
						try
						{
							LoginSucceeded?.Invoke();
						}
						catch (Exception e)
						{
							DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - LoginSucceeded invocation error: {1}", ConnectionId, e);
							_analytics.TrackError("LoginSucceeded invocation error", e);
						}
					});
				}
			}
			else
			{
				DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - Login Failed", ConnectionId);

				if (LoginFailed != null)
				{
					RunInMainThread(() =>
					{
						try
						{
							LoginFailed?.Invoke(errorCode);
						}
						catch (Exception e)
						{
							DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - LoginFailed invocation error: {1}", ConnectionId, e);
							_analytics.TrackError("LoginFailed invocation error", e);
						}
					});
				}
			}
		}

		private void InvokeMessageReceived(string messageType, string errorCode, int requestId, SnipeObject responseData)
		{
			if (MessageReceived != null)
			{
				RunInMainThread(() =>
				{
					try
					{
						MessageReceived?.Invoke(messageType, errorCode, responseData, requestId);
					}
					catch (Exception e)
					{
						DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - {1} - MessageReceived invocation error: {2}", ConnectionId, messageType, e);
						_analytics.TrackError("MessageReceived invocation error", e, new Dictionary<string, object>()
						{
							["messageType"] = messageType,
							["errorCode"] = errorCode,
						});
					}
				});
			}
			else
			{
				DebugLogger.LogFormat("[SnipeClient] [{0}] ProcessMessage - no MessageReceived listeners", ConnectionId);
			}
		}

		private void FlushBatchedRequests()
		{
			if (_batchedRequests == null || _batchedRequests.IsEmpty)
			{
				return;
			}

			lock (_batchLock)
			{
				if (_batchedRequests.Count == 1)
				{
					if (_batchedRequests.TryDequeue(out SnipeObject message))
					{
						DoSendRequest(message);
					}
				}
				else
				{
					List<SnipeObject> messages = new List<SnipeObject>(_batchedRequests.Count);
					while (_batchedRequests.TryDequeue(out SnipeObject message))
					{
						messages.Add(message);

						DebugLogger.LogFormat("[SnipeClient] Request batched - {0}", message.ToJSONString());
					}
					DoSendBatch(messages);
				}
			}
		}
	}
}
