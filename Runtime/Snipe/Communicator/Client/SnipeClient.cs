using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public sealed class SnipeClient : IDisposable
	{
		public const int SNIPE_VERSION = 6;
		public const int MAX_BATCH_SIZE = 5;
		public const int UNAUTHORIZED_REQUESTS_PER_SECOND_LIMIT = 5;
		public const int MAX_PENDING_REQUESTS_COUNT = 100;
		public const int MIN_RATE_LIMIT_RETRY_DELAY_MS = 1000;
		public const int MAX_RATE_LIMIT_RETRY_DELAY_MS = 10000;

		public delegate void MessageReceivedHandler(string messageType, string errorCode, IDictionary<string, object> data, int requestID);

		public event MessageReceivedHandler MessageReceived;
		public event Action<TransportInfo> ConnectionOpened;
		public event Action<TransportInfo> ConnectionClosed;
		public event Action ConnectionDisrupted;
		public event Action<TransportInfo> UdpConnectionFailed;

		public bool Connected => _transportService.Connected;
		public bool LoggedIn => _loggedIn && Connected;
		public bool WebSocketConnected => _transportService.WebSocketConnected;
		public bool UdpClientConnected => _transportService.UdpClientConnected;
		public bool HttpClientConnected => _transportService.HttpClientConnected;

		public string ConnectionId { get; private set; }

		private readonly TransportService _transportService;
		private bool _loggedIn = false;
		private long _serverReactionStartTimestamp;
		public TimeSpan CurrentRequestElapsed => StopwatchUtil.GetElapsedTime(_serverReactionStartTimestamp);

		public bool BatchMode
		{
			get => _batchBuffer.Enabled;
			set
			{
				if (value == BatchMode)
				{
					return;
				}

				SendBatchedRequests(_batchBuffer.SetEnabled(value));

				_logger.LogDebug($"BatchMode = {value}");
			}
		}

		private int _requestId = 0;

		private readonly SnipeOptions _options;
		private readonly IAnalyticsContext _analytics;
		private readonly ResponseMonitor _responseMonitor;
		private readonly SnipeRequestBatchBuffer _batchBuffer;
		private readonly SnipeRequestDispatcher _dispatcher;
		private readonly ILogger _logger;

		internal SnipeClient(SnipeOptions options, ISnipeServices services)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			_options = options;
			_analytics = (services.Analytics as IAnalyticsTrackerProvider)?.GetTracker(options.ContextId);
			_responseMonitor = new ResponseMonitor(_analytics, services);
			_logger = services.LoggerFactory.CreateLogger(nameof(SnipeClient));
			_transportService = new TransportService(options, _analytics, services);
			_batchBuffer = new SnipeRequestBatchBuffer();

			_dispatcher = new SnipeRequestDispatcher(new SnipeRequestDispatcherOptions()
			{
				SendRequest = SendRequestNow,
				SendBatch = SendBatchNow,
				IsConnected = () => Connected,
				OnPendingQueueOverflow = Disconnect,
				Analytics = _analytics,
				Logger = services.LoggerFactory.CreateLogger(nameof(SnipeRequestDispatcher)),
				GetRequestsPerSecondLimit = GetRequestsPerSecondLimit,
			});

			_transportService.ConnectionOpened += OnTransportConnectionOpened;
			_transportService.ConnectionClosed += OnTransportConnectionClosed;
			_transportService.ConnectionDisrupted += OnTransportConnectionDisrupted;
			_transportService.UdpConnectionFailed += () => UdpConnectionFailed?.Invoke(_transportService.GetTransportInfo());
			_transportService.MessageReceived += ProcessMessage;
		}

		public TransportInfo Connect()
		{
			_transportService.InitializeTransports();
			_transportService.TryStartNextTransport();
			return _transportService.GetTransportInfo();
		}

		public Transport GetTransport() => _transportService.GetCurrentTransport();

		private void OnTransportConnectionOpened(Transport transport)
		{
			ConnectionOpened?.Invoke(transport.Info);
		}

		private void OnTransportConnectionClosed(TransportInfo transportInfo)
		{
			ClearConnectionInfo();
			ConnectionClosed?.Invoke(transportInfo);
		}

		private void OnTransportConnectionDisrupted(Transport transport)
		{
			ClearConnectionInfo();
			ConnectionDisrupted?.Invoke();
		}

		public void Disconnect()
		{
			Disconnect(true);
		}

		private void Disconnect(bool raiseEvent)
		{
			ClearConnectionInfo();
			_transportService.StopCurrentTransport();
			if (raiseEvent)
			{
				_transportService.RaiseConnectionClosedEvent();
			}
		}

		private void ClearConnectionInfo()
		{
			_loggedIn = false;
			ConnectionId = "";
			_responseMonitor.Stop();
			_batchBuffer.Clear();
			_dispatcher.Clear();
		}

		private int GetRequestsPerSecondLimit()
		{
			// Login and handshake requests use a smaller fixed limit until server auth succeeds.
			return LoggedIn ? _options.RequestsPerSecondLimit : UNAUTHORIZED_REQUESTS_PER_SECOND_LIMIT;
		}

		public int SendRequest(string messageType, IDictionary<string, object> data)
		{
			if (!Connected)
			{
				return 0;
			}

			var message = new Dictionary<string, object>() { ["t"] = messageType };

			if (data != null)
			{
				message.Add("data", data);
			}

			return InternalSendRequest(messageType, message);
		}

		public int SendRequest(IDictionary<string, object> message)
		{
			if (!Connected || message == null)
			{
				return 0;
			}

			return InternalSendRequest(null, message);
		}

		private int InternalSendRequest(string messageType, IDictionary<string, object> message)
		{
#if SNIPE_PROFILEMENAGER
			messageType ??= message.SafeGetString("t");
			SnipeProfileManagerRequestGuard.TrackForbiddenRequest(messageType, _analytics, _logger);
#endif

			int requestId = ++_requestId;
			message["id"] = requestId;

			IDictionary<string, object> data = null;

			if (!_loggedIn && _batchBuffer.IsEmpty)
			{
				SnipeRequestMessageDataHelper.Ensure(ref data, message);
				data["ckey"] = _options.ClientKey;
				message["data"] = data;
			}

			if (_options.DebugId != null)
			{
				SnipeRequestMessageDataHelper.Ensure(ref data, message);
				data["debugID"] = _options.DebugId;
			}

			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug("Request registered: {0}", JsonUtility.ToJson(message));
			}

			if (BatchMode)
			{
				SendBatchedRequests(_batchBuffer.Add(message));
			}
			else
			{
				_dispatcher.Send(message, true);
			}

			return requestId;
		}

		private bool SendRequestNow(IDictionary<string, object> message)
		{
			if (!Connected || message == null)
			{
				return false;
			}

			if (_logger.IsEnabled(LogLevel.Debug))
			{
				LogShortMessageInfo("SendRequest", message);
			}

			_transportService.SendMessage(message);

			_serverReactionStartTimestamp = Stopwatch.GetTimestamp();
			TrackResponse(message);
			return true;
		}

		private void TrackResponse(IDictionary<string, object> message)
		{
			int id = message.SafeGetValue<int>("id");
			_responseMonitor.Add(id, message.SafeGetString("t"));
		}

		private bool SendBatchNow(List<IDictionary<string, object>> messages)
		{
			if (!Connected || messages == null || messages.Count == 0)
			{
				return false;
			}

			if (_logger.IsEnabled(LogLevel.Debug))
			{
				var msgs = new string[messages.Count];
				for (int i = 0; i < messages.Count; i++)
				{
					var m = messages[i];
					msgs[i] = "(" + m.SafeGetString("id") + " - " + m.SafeGetString("t") + ")";
				}
				_logger.LogDebug("SendBatch - {0} items: {1}", messages.Count, string.Join(", ", msgs));
			}

			_transportService.SendBatch(messages);

			_serverReactionStartTimestamp = Stopwatch.GetTimestamp();

			for (int i = 0; i < messages.Count; i++)
			{
				TrackResponse(messages[i]);
			}

			return true;
		}

		private void ProcessMessage(IDictionary<string, object> message)
		{
			if (message == null)
			{
				return;
			}

			if (_serverReactionStartTimestamp > 0)
			{
				_analytics.ServerReaction = StopwatchUtil.GetElapsedTime(_serverReactionStartTimestamp);
			}

			string messageType = message.SafeGetString("t");
			string errorCode = message.SafeGetString("errorCode");
			int requestID = message.SafeGetValue<int>("id");
			int ackID = message.SafeGetValue<int>("ackID");
			IDictionary<string, object> responseData = message.SafeGetValue<IDictionary<string, object>>("data");

			_responseMonitor.Remove(requestID, messageType);

			if (_logger.IsEnabled(LogLevel.Debug))
			{
				string dataJson = responseData != null ? JsonUtility.ToJson(responseData) : null;
				_logger.LogDebug("[{0}] ProcessMessage - {1} - {2} {3} {4}", ConnectionId, requestID, messageType, errorCode, dataJson);
			}

			// Retryable rate-limit responses are internal; final success/error is still delivered normally.
			bool retryRateLimitedRequest = errorCode == SnipeErrorCodes.RATE_LIMIT &&
				requestID > 0 &&
				_dispatcher.TryHandleRateLimit(requestID);

			if (!retryRateLimitedRequest)
			{
				_dispatcher.RemoveSent(requestID);

				if (!_loggedIn && messageType == SnipeMessageTypes.USER_LOGIN)
				{
					ProcessLoginResponse(errorCode, responseData);
				}

				InvokeMessageReceived(messageType, errorCode, requestID, responseData);
			}

			if (ackID > 0)
			{
				// Backend supports delayed and batched ACKs, so route them through the dispatcher.
				SendRequest("ack.ack", new Dictionary<string, object>() { ["ackID"] = ackID });
			}
		}

		private void ProcessLoginResponse(string errorCode, IDictionary<string, object> responseData)
		{
			if (errorCode == SnipeErrorCodes.OK || errorCode == SnipeErrorCodes.ALREADY_LOGGED_IN)
			{
				_logger.LogDebug("[{0}] ProcessMessage - Login Succeeded", ConnectionId);

				_loggedIn = true;
				_transportService.SetLoggedIn();

				if (responseData != null)
				{
					ConnectionId = responseData.SafeGetString("connectionID");
				}
				else
				{
					ConnectionId = "";
				}
			}
			else
			{
				_logger.LogDebug("[{0}] ProcessMessage - Login Failed", ConnectionId);
			}
		}

		private void InvokeMessageReceived(string messageType, string errorCode, int requestId, IDictionary<string, object> responseData)
		{
			if (MessageReceived != null)
			{
				var data = responseData != null ? new Dictionary<string, object>(responseData) : new Dictionary<string, object>(0);
				MessageReceived?.Invoke(messageType, errorCode, data, requestId);
			}
			else
			{
				_logger.LogDebug("[{0}] ProcessMessage - no MessageReceived listeners", ConnectionId);
			}
		}

		private void SendBatchedRequests(List<IDictionary<string, object>> messages)
		{
			if (messages == null || messages.Count == 0)
			{
				return;
			}

			if (messages.Count == 1)
			{
				_dispatcher.Send(messages[0], false);
			}
			else
			{
				if (_logger.IsEnabled(LogLevel.Debug))
				{
					for (int i = 0; i < messages.Count; i++)
					{
						LogShortMessageInfo("Request batched", messages[i]);
					}
				}

				_dispatcher.SendBatch(messages);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void LogShortMessageInfo(string prefix, IDictionary<string, object> message)
		{
			_logger.LogDebug("{0} - {1} - {2}", prefix, message.SafeGetString("id"), message.SafeGetString("t"));
		}

		public void Dispose()
		{
			Disconnect(false);
			_dispatcher.Dispose();
			_responseMonitor.Dispose();
			_transportService.Dispose();
		}
	}
}
