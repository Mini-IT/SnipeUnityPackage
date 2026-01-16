using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class SnipeClient : IDisposable
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
		public event Action ConnectionDisrupted;

		private sealed class TransportEntry
		{
			public Transport Instance;
			public Func<Transport> Factory;
			public Func<(string endpoint, ushort port)> ResolveEndpoint;
			public Func<bool> TryAdvanceUrl;
		}

		private Transport _transport;
		private TransportInfo _currentTransportInfo;

		private bool _loggedIn = false;

		public bool Connected => _transport != null && _transport.Connected;
		public bool LoggedIn { get { return _loggedIn && Connected; } }
		public bool WebSocketConnected => Connected && _transport is WebSocketTransport;
		public bool UdpClientConnected => Connected && _transport is KcpTransport;
		public bool HttpClientConnected => Connected && _transport is HttpTransport;

		public string ConnectionId { get; private set; }

		private long _connectionStartTimestamp;

		private long _serverReactionStartTimestamp;
		public TimeSpan CurrentRequestElapsed => GetElapsedTime(_serverReactionStartTimestamp);

		public bool BatchMode
		{
			get => _batchedRequests != null;
			set
			{
				if (value == BatchMode)
				{
					return;
				}

				if (value)
				{
					_batchedRequests ??= new ConcurrentQueue<SnipeObject>();
				}
				else
				{
					FlushBatchedRequests();
					_batchedRequests = null;
				}

				_logger.LogTrace($"BatchMode = {value}");
			}
		}

		private ConcurrentQueue<SnipeObject> _batchedRequests;
		private readonly object _batchLock = new object();

		private readonly List<TransportEntry> _transportEntries = new List<TransportEntry>(3);
		private int _currentTransportIndex;
		private bool _retryCurrentTransport;

		private int _requestId = 0;

		private readonly SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;
		private readonly ResponseMonitor _responseMonitor;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly ILogger _logger;

		internal SnipeClient(SnipeConfig config)
		{
			_config = config;
			_analytics = SnipeServices.Analytics.GetTracker(config.ContextId);
			_responseMonitor = new ResponseMonitor(_analytics);
			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_logger = SnipeServices.LogService.GetLogger(nameof(SnipeClient));
		}

		public void Connect()
		{
			InitializeTransports();
			TryStartNextTransport();
		}

		public TransportInfo GetTransportInfo() => _currentTransportInfo;

		public void InitializeTransports()
		{
			_currentTransportIndex = -1;
			_retryCurrentTransport = false;

			for (int i = 0; i < _transportEntries.Count; i++)
			{
				if (_transportEntries[i] != null)
				{
					// We already have at least one entry.
					// It means that the initialization is already done.
					return;
				}
			}

#if !UNITY_WEBGL
			if (_config.CheckUdpAvailable())
			{
				_transportEntries.Add(new TransportEntry
				{
					Factory = CreateKcpTransport,
					ResolveEndpoint = () =>
					{
						var address = _config.GetUdpAddress();
						return address == null ? (null, 0) : (address.Host, address.Port);
					},
					TryAdvanceUrl = () => _config.NextUdpUrl()
				});
			}
#endif

			if (_config.CheckWebSocketAvailable())
			{
				_transportEntries.Add(new TransportEntry
				{
					Factory = CreateWebSocketTransport,
					ResolveEndpoint = () => (_config.GetWebSocketUrl(), 0),
					TryAdvanceUrl = () => _config.NextWebSocketUrl()
				});
			}

			if (_config.CheckHttpAvailable())
			{
				_transportEntries.Add(new TransportEntry
				{
					Factory = CreateHttpTransport,
					ResolveEndpoint = () => (_config.GetHttpAddress(), 0),
					TryAdvanceUrl = () => _config.NextHttpUrl()
				});
			}
		}

		public bool TryStartNextTransport()
		{
			if (_transportEntries.Count == 0)
			{
				return false;
			}

			if (_transport != null && _transport.Started)
			{
				return false;
			}

			while (true)
			{
				var entry = GetEntryToStart();
				if (entry == null)
				{
					return false;
				}

				if (entry.Instance == null)
				{
					entry.Instance = entry.Factory.Invoke();
				}

				_transport = entry.Instance;

				var (endpoint, port) = entry.ResolveEndpoint();
				if (string.IsNullOrEmpty(endpoint) || (_transport is KcpTransport && port == 0))
				{
					DisposeEntry(entry);
					continue;
				}

				_connectionStartTimestamp = Stopwatch.GetTimestamp();
				_transport.Connect(endpoint, port);

				return true;
			}
		}

		#region Transport factories

		private KcpTransport CreateKcpTransport()
		{
			var transport = new KcpTransport(_config, _analytics);

			transport.ConnectionOpenedHandler = (Transport t) =>
			{
				_analytics.UdpConnectionTime = GetElapsedTime(_connectionStartTimestamp);
				OnTransportConnectionOpened(t);
			};

			transport.ConnectionClosedHandler = (Transport t) =>
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					UdpConnectionFailed?.Invoke();
				});

				OnTransportConnectionClosed(t);
			};

			transport.MessageReceivedHandler = ProcessMessage;

			return transport;
		}

		private WebSocketTransport CreateWebSocketTransport()
		{
			return new WebSocketTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = OnTransportConnectionOpened,
				ConnectionClosedHandler = OnTransportConnectionClosed,
				MessageReceivedHandler = ProcessMessage
			};
		}

		private HttpTransport CreateHttpTransport()
		{
			return new HttpTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = OnTransportConnectionOpened,
				ConnectionClosedHandler = OnTransportConnectionClosed,
				MessageReceivedHandler = ProcessMessage,
			};
		}

		#endregion

		private void OnTransportConnectionOpened(Transport transport)
		{
			_currentTransportInfo = transport.Info;
			_analytics.ConnectionEstablishmentTime = GetElapsedTime(_connectionStartTimestamp);

			_mainThreadRunner.RunInMainThread(() =>
			{
				try
				{
					ConnectionOpened?.Invoke();
				}
				catch (Exception e)
				{
					_logger.LogTrace("ConnectionOpened invocation error: {0}", e);
					_analytics.TrackError("ConnectionOpened invocation error", e);
				}
			});
		}

		private void OnTransportConnectionClosed(Transport transport)
		{
			if (transport != _transport)
			{
				return;
			}

			var entry = GetCurrentEntry();

			if (transport is KcpTransport)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					UdpConnectionFailed?.Invoke();
				});
			}

			_currentTransportInfo = transport.Info;
			_transport = null;
			_loggedIn = false;
			ResetAnalyticsMetrics();

			bool hasMoreUrls = entry != null && entry.TryAdvanceUrl();
			if (!hasMoreUrls && entry != null)
			{
				DisposeEntry(entry);
			}

			_mainThreadRunner.RunInMainThread(() =>
			{
				ConnectionDisrupted?.Invoke();
			});

			_retryCurrentTransport = hasMoreUrls;

			if (!TryStartNextTransport())
			{
				FinishConnectionAttempts();
			}
		}

		private void RaiseConnectionClosedEvent()
		{
			_mainThreadRunner.RunInMainThread(() =>
			{
				try
				{
					ConnectionClosed?.Invoke();
				}
				catch (Exception e)
				{
					_logger.LogTrace("ConnectionClosed invocation error: {0}", e);
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

			_responseMonitor.Stop();

			DisposeEntries();
			_currentTransportIndex = -1;
			_retryCurrentTransport = false;

			ResetAnalyticsMetrics();

			if (raiseEvent)
			{
				RaiseConnectionClosedEvent();
			}
		}

		private TransportEntry GetEntryToStart()
		{
			if (_retryCurrentTransport)
			{
				_retryCurrentTransport = false;
				var current = GetCurrentEntry();
				if (current != null)
				{
					return current;
				}
			}

			if (!MoveToNextEntry())
			{
				return null;
			}

			return GetCurrentEntry();
		}

		private bool MoveToNextEntry()
		{
			int nextIndex = _currentTransportIndex;
			while (true)
			{
				nextIndex++;
				if (nextIndex >= _transportEntries.Count)
				{
					return false;
				}

				if (_transportEntries[nextIndex] != null)
				{
					_currentTransportIndex = nextIndex;
					return true;
				}
			}
		}

		private TransportEntry GetCurrentEntry()
		{
			if (_currentTransportIndex < 0 || _currentTransportIndex >= _transportEntries.Count)
			{
				return null;
			}

			return _transportEntries[_currentTransportIndex];
		}

		private void DisposeEntry(TransportEntry entry)
		{
			if (entry?.Instance == null)
			{
				return;
			}

			entry.Instance.Dispose();

			if (_transport == entry.Instance)
			{
				_transport = null;
			}

			entry.Instance = null;

			// Remove exhausted entry from list
			for (int i = 0; i < _transportEntries.Count; i++)
			{
				if (_transportEntries[i] == entry)
				{
					_transportEntries[i] = null;
					break;
				}
			}
		}

		private void DisposeEntries()
		{
			// Create a copy to avoid modifying collection during enumeration
			var entriesToDispose = _transportEntries.ToArray();
			// And clear the original list to avoid the inner loop work in `DisposeEntry`
			_transportEntries.Clear();

			foreach (var entry in entriesToDispose)
			{
				DisposeEntry(entry);
			}
		}

		private void ResetAnalyticsMetrics()
		{
			_analytics.PingTime = TimeSpan.Zero;
			_analytics.ServerReaction = TimeSpan.Zero;
		}

		private void FinishConnectionAttempts()
		{
			DisposeEntries();
			_transport = null;
			_currentTransportIndex = -1;
			_retryCurrentTransport = false;
			ResetAnalyticsMetrics();
			RaiseConnectionClosedEvent();
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
				_batchedRequests!.Enqueue(message);

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

			if (_logger.IsEnabled(LogLevel.Trace))
			{
				_logger.LogTrace("SendRequest - {0}", message.ToJSONString());
			}

			_transport.SendMessage(message);

			_serverReactionStartTimestamp = Stopwatch.GetTimestamp();

			int id = message.SafeGetValue<int>("id");
			_responseMonitor.Add(id, message.SafeGetString("t"));
		}

		private void DoSendBatch(List<SnipeObject> messages)
		{
			if (!Connected || messages == null || messages.Count == 0)
				return;

			_logger.LogTrace("DoSendBatch - {0} items", messages.Count);

			_transport.SendBatch(messages);
		}

		private void ProcessMessage(SnipeObject message)
		{
			if (message == null)
			{
				return;
			}

			if (_serverReactionStartTimestamp > 0)
			{
				_analytics.ServerReaction = GetElapsedTime(_serverReactionStartTimestamp);
			}

			string messageType = message.SafeGetString("t");
			string errorCode = message.SafeGetString("errorCode");
			int requestID = message.SafeGetValue<int>("id");
			int ackID = message.SafeGetValue<int>("ackID");
			SnipeObject responseData = message.SafeGetValue<SnipeObject>("data");

			_responseMonitor.Remove(requestID, messageType);

			if (_logger.IsEnabled(LogLevel.Trace))
			{
				_logger.LogTrace("[{0}] ProcessMessage - {1} - {2} {3} {4}", ConnectionId, requestID, messageType, errorCode, responseData?.ToFastJSONString());
			}

			if (!_loggedIn && messageType == SnipeMessageTypes.USER_LOGIN)
			{
				ProcessLoginResponse(errorCode, responseData);
			}

			InvokeMessageReceived(messageType, errorCode, requestID, responseData);

			if (ackID > 0)
			{
				SendRequest("ack.ack", new SnipeObject() { ["ackID"] = ackID });
			}
		}

		private void ProcessLoginResponse(string errorCode, SnipeObject responseData)
		{
			if (errorCode == SnipeErrorCodes.OK || errorCode == SnipeErrorCodes.ALREADY_LOGGED_IN)
			{
				_logger.LogTrace("[{0}] ProcessMessage - Login Succeeded", ConnectionId);

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
					_mainThreadRunner.RunInMainThread(() =>
					{
						try
						{
							LoginSucceeded?.Invoke();
						}
						catch (Exception e)
						{
							_logger.LogTrace("[{0}] ProcessMessage - LoginSucceeded invocation error: {1}", ConnectionId, e);
							_analytics.TrackError("LoginSucceeded invocation error", e);
						}
					});
				}
			}
			else
			{
				_logger.LogTrace("[{0}] ProcessMessage - Login Failed", ConnectionId);

				if (LoginFailed != null)
				{
					_mainThreadRunner.RunInMainThread(() =>
					{
						try
						{
							LoginFailed?.Invoke(errorCode);
						}
						catch (Exception e)
						{
							_logger.LogTrace("[{0}] ProcessMessage - LoginFailed invocation error: {1}", ConnectionId, e);
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
				_mainThreadRunner.RunInMainThread(() =>
				{
					try
					{
						MessageReceived?.Invoke(messageType, errorCode, responseData, requestId);
					}
					catch (Exception e)
					{
						_logger.LogTrace("[{0}] ProcessMessage - {1} - MessageReceived invocation error: {2}", ConnectionId, messageType, e);
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
				_logger.LogTrace("[{0}] ProcessMessage - no MessageReceived listeners", ConnectionId);
			}
		}

		private void FlushBatchedRequests()
		{
			ReadOnlySpan<SnipeObject> queue;

			lock (_batchLock)
			{
				if (_batchedRequests == null || _batchedRequests.IsEmpty)
				{
					return;
				}

				// local copy for thread safety
				queue = _batchedRequests.ToArray();
				_batchedRequests.Clear();
			}

			if (queue.Length == 1)
			{
				DoSendRequest(queue[0]);
			}
			else
			{
				var messages = new List<SnipeObject>(queue.Length);

				for (int i = 0; i < queue.Length; i++)
				{
					var message = queue[i];
					messages.Add(message);
					_logger.LogTrace("Request batched - {0}", message.ToJSONString());
				}

				DoSendBatch(messages);
			}
		}

		// Stopwatch.GetElapsedTime() is added only in .NET 7+
		// Here is a custom implementation
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static TimeSpan GetElapsedTime(long startTS)
		{
			return (startTS > 0) ? TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTS) : TimeSpan.Zero;
		}

		public void Dispose()
		{
			Disconnect(false);
			_transportEntries.Clear();
			_responseMonitor.Dispose();
		}
	}
}
