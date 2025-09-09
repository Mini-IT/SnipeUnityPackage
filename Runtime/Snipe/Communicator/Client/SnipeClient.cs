using System;
using System.Collections.Concurrent;
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
			get => _batchedRequests != null;
			set
			{
				if (value == BatchMode)
				{
					return;
				}

				if (value)
				{
					_batchedRequests ??= new ConcurrentQueue<IDictionary<string, object>>();
				}
				else
				{
					FlushBatchedRequests();
					_batchedRequests = null;
				}

				_logger.LogTrace($"BatchMode = {value}");
			}
		}

		private ConcurrentQueue<IDictionary<string, object>> _batchedRequests;
		private readonly object _batchLock = new object();

		private int _requestId = 0;

		private readonly SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;
		private readonly ResponseMonitor _responseMonitor;
		private readonly ILogger _logger;

		internal SnipeClient(SnipeConfig config)
		{
			_config = config;
			_analytics = SnipeServices.Analytics.GetTracker(config.ContextId);
			_responseMonitor = new ResponseMonitor(_analytics);
			_logger = SnipeServices.LogService.GetLogger(nameof(SnipeClient));
			_transportService = new TransportService(config, _analytics);

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

		private void OnTransportConnectionClosed(Transport transport)
		{
			ClearConnectionInfo();
			ConnectionClosed?.Invoke(transport?.Info ?? default);
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
			_transportService.StopCurrentTransport(raiseEvent);
		}

		private void ClearConnectionInfo()
		{
			_loggedIn = false;
			ConnectionId = "";
			_responseMonitor.Stop();
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

			return SendRequest(message);
		}

		public int SendRequest(IDictionary<string, object> message)
		{
			if (!Connected || message == null)
			{
				return 0;
			}

			int requestId = ++_requestId;
			message["id"] = requestId;

			IDictionary<string, object> data = null;

			if (!_loggedIn && (_batchedRequests == null || _batchedRequests.IsEmpty))
			{
				EnsureMessageData(ref data, message);
				data["ckey"] = _config.ClientKey;
				message["data"] = data;
			}

			if (_config.DebugId != null)
			{
				EnsureMessageData(ref data, message);
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

		private void DoSendRequest(IDictionary<string, object> message)
		{
			if (!Connected || message == null)
			{
				return;
			}

			if (_logger.IsEnabled(LogLevel.Trace))
			{
				_logger.LogTrace("SendRequest - {0}", JsonUtility.ToJson(message));
			}

			_transportService.SendMessage(message);

			_serverReactionStartTimestamp = Stopwatch.GetTimestamp();

			int id = message.SafeGetValue<int>("id");
			_responseMonitor.Add(id, message.SafeGetString("t"));
		}

		private void DoSendBatch(List<IDictionary<string, object>> messages)
		{
			if (!Connected || messages == null || messages.Count == 0)
				return;

			_logger.LogTrace("DoSendBatch - {0} items", messages.Count);

			_transportService.SendBatch(messages);
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

			if (_logger.IsEnabled(LogLevel.Trace))
			{
				string dataJson = responseData != null ? JsonUtility.ToJson(responseData) : null;
				_logger.LogTrace("[{0}] ProcessMessage - {1} - {2} {3} {4}", ConnectionId, requestID, messageType, errorCode, dataJson);
			}

			if (!_loggedIn && messageType == SnipeMessageTypes.USER_LOGIN)
			{
				ProcessLoginResponse(errorCode, responseData);
			}

			InvokeMessageReceived(messageType, errorCode, requestID, responseData);

			if (ackID > 0)
			{
				SendRequest("ack.ack", new Dictionary<string, object>() { ["ackID"] = ackID });
			}
		}

		private void ProcessLoginResponse(string errorCode, IDictionary<string, object> responseData)
		{
			if (errorCode == SnipeErrorCodes.OK || errorCode == SnipeErrorCodes.ALREADY_LOGGED_IN)
			{
				_logger.LogTrace("[{0}] ProcessMessage - Login Succeeded", ConnectionId);

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
				_logger.LogTrace("[{0}] ProcessMessage - Login Failed", ConnectionId);
			}
		}

		private void InvokeMessageReceived(string messageType, string errorCode, int requestId, IDictionary<string, object> responseData)
		{
			if (MessageReceived != null)
			{
				MessageReceived?.Invoke(messageType, errorCode, new Dictionary<string, object>(responseData), requestId);
			}
			else
			{
				_logger.LogTrace("[{0}] ProcessMessage - no MessageReceived listeners", ConnectionId);
			}
		}

		private void FlushBatchedRequests()
		{
			ReadOnlySpan<IDictionary<string, object>> queue;

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
				var messages = new List<IDictionary<string, object>>(queue.Length);

				for (int i = 0; i < queue.Length; i++)
				{
					var message = queue[i];
					messages.Add(message);
					_logger.LogTrace("Request batched - {0}", JsonUtility.ToJson(message));
				}

				DoSendBatch(messages);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void EnsureMessageData(ref IDictionary<string, object> data, IDictionary<string, object> message)
		{
			if (data != null)
			{
				return;
			}

			if (message.TryGetValue("data", out var dataObj))
			{
				data = dataObj as Dictionary<string, object> ?? new Dictionary<string, object>();
			}
			else
			{
				data = new Dictionary<string, object>();
			}
		}

		public void Dispose()
		{
			Disconnect(false);
			_responseMonitor.Dispose();
			_transportService.Dispose();
		}
	}
}
