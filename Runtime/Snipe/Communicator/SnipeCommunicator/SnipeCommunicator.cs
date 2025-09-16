using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed partial class SnipeCommunicator : IDisposable
	{
		public readonly int InstanceId = new System.Random().Next();

		private const int RETRY_INIT_CLIENT_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MIN_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MAX_DELAY = 10000; // ms
		private const int RETRY_INIT_CLIENT_RANDOM_DELAY = 500; // ms

		public string ConnectionId => _client?.ConnectionId;

		public bool AllowRequestsToWaitForLogin { get; set; } = true;

		public int RestoreConnectionAttempts = 3;
		private int _restoreConnectionAttempt;

		private List<AbstractCommunicatorRequest> _requests;
		public List<AbstractCommunicatorRequest> Requests => _requests ??= new List<AbstractCommunicatorRequest>();

		public HashSet<SnipeRequestDescriptor> MergeableRequestTypes { get; } = new HashSet<SnipeRequestDescriptor>();

		public bool Connected => _client != null && _client.Connected;
		public bool LoggedIn => _client != null && _client.LoggedIn;

		public bool? RoomJoined => (_client == null) ? null : (_client.LoggedIn && _roomStateObserver.State == RoomState.Joined);

		public bool BatchMode
		{
			get => _client?.BatchMode ?? false;
			set
			{
				if (_client != null)
				{
					_client.BatchMode = value;
				}
			}
		}

		private SnipeClient _client;
		private bool _disconnecting = false;

		private readonly object _clientLock = new object();

		private CancellationTokenSource _delayedInitCancellation;

		private SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly RoomStateObserver _roomStateObserver;
		private readonly ILogger _logger;

		public SnipeCommunicator(SnipeAnalyticsTracker analytics)
		{
			_analytics = analytics;

			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_logger = SnipeServices.LogService.GetLogger(nameof(SnipeCommunicator));
			_logger.BeginScope($"{InstanceId}");

			_roomStateObserver = new RoomStateObserver(this);

			_logger.LogInformation($"PACKAGE VERSION: {PackageInfo.VERSION_NAME}");
		}

		public void Initialize(SnipeConfig config)
		{
			_config = config;
		}

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void Start()
		{
			if (InitClient())
			{
				_restoreConnectionAttempt = 0;
			}
		}

		private bool InitClient()
		{
			if (_delayedInitCancellation != null)
			{
				_delayedInitCancellation.Cancel();
				_delayedInitCancellation = null;
			}

			if (LoggedIn)
			{
				_logger.LogWarning("InitClient - already logged in");
				return false;
			}

			lock (_clientLock)
			{
				if (_client == null)
				{
					_client = new SnipeClient(_config);
					_client.ConnectionOpened += OnClientConnectionOpened;
					_client.ConnectionClosed += OnClientConnectionClosed;
					_client.ConnectionDisrupted += OnClientConnectionDisrupted;
					_client.UdpConnectionFailed += OnClientUdpConnectionFailed;
					_client.MessageReceived += OnMessageReceived;
				}

				if (_client.Connected)
				{
					return false;
				}

				_disconnecting = false;

				var transportInfo = _client.Connect();

				_mainThreadRunner.RunInMainThread(() =>
				{
					AnalyticsTrackStartConnection(transportInfo);
				});
			}

			return true;
		}

		public void Disconnect()
		{
			_logger.LogTrace("Disconnect");

			_roomStateObserver.Reset();
			_disconnecting = true;

			if (_delayedInitCancellation != null)
			{
				_delayedInitCancellation.Cancel();
				_delayedInitCancellation = null;
			}

			if (_client != null && _client.Connected)
			{
				_client.Disconnect();
			}
		}

		internal int SendRequest(string messageType, IDictionary<string, object> data)
		{
			int id = _client?.SendRequest(messageType, data) ?? 0;

			if (id != 0)
			{
				_roomStateObserver.OnRequestSent(messageType);
			}

			return id;
		}

		private void OnClientConnectionOpened(TransportInfo transportInfo)
		{
			_logger.LogTrace("Client connection opened");

			_restoreConnectionAttempt = 0;
			_disconnecting = false;
			_analytics.ConnectionEventsEnabled = true;

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded(transportInfo);
				ConnectionEstablished?.Invoke();
			});
		}

		private void OnClientConnectionClosed(TransportInfo transportInfo)
		{
			_logger.LogTrace($"[{_client?.ConnectionId}] Client connection closed");

			_roomStateObserver.Reset();

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed(transportInfo);
				OnConnectionFailed();
			});
		}

		private void OnClientUdpConnectionFailed(TransportInfo transportInfo)
		{
			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackUdpConnectionFailed(transportInfo);
			});
		}

		// Not main thread
		private void OnClientConnectionDisrupted()
		{
			_logger.LogTrace($"({InstanceId}) [{_client?.ConnectionId}] Client connection disrupted");

			DisposeRequests();

			_mainThreadRunner.RunInMainThread(() =>
			{
				ConnectionDisrupted?.Invoke();
			});
		}

		// Main thread
		private void OnConnectionFailed()
		{
			OnClientConnectionDisrupted();

			if (_restoreConnectionAttempt < RestoreConnectionAttempts && !_disconnecting)
			{
				ReconnectionScheduled?.Invoke();

				AttemptToRestoreConnection();
				return;
			}

			ConnectionClosed?.Invoke();
		}

		private void AttemptToRestoreConnection()
		{
			_restoreConnectionAttempt++;
			_logger.LogTrace($"Attempt to restore connection {_restoreConnectionAttempt}");

			_analytics.ConnectionEventsEnabled = false;

			if (_delayedInitCancellation == null)
			{
				_delayedInitCancellation = new CancellationTokenSource();
				DelayedInitClient(_delayedInitCancellation.Token);
			}
		}

		private void OnMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestID)
		{
			_roomStateObserver.OnMessageReceived(messageType, errorCode);

			if (MessageReceived != null)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					RaiseEvent(MessageReceived, messageType, errorCode, data, requestID);
				});
			}

			if (errorCode != SnipeErrorCodes.OK)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					_analytics.TrackErrorCodeNotOk(messageType, errorCode, data);
				});
			}
		}

		private async void DelayedInitClient(CancellationToken cancellation)
		{
			_logger.LogTrace("WaitAndInitClient - start delay");

			Random random = new Random();
			int delay = RETRY_INIT_CLIENT_DELAY * _restoreConnectionAttempt + random.Next(RETRY_INIT_CLIENT_RANDOM_DELAY);
			if (delay < RETRY_INIT_CLIENT_MIN_DELAY)
				delay = RETRY_INIT_CLIENT_MIN_DELAY;
			else if (delay > RETRY_INIT_CLIENT_MAX_DELAY)
				delay = RETRY_INIT_CLIENT_MAX_DELAY;

			try
			{
				await AlterTask.Delay(delay, cancellation);
			}
			catch (Exception)
			{
				return;
			}
			if (cancellation.IsCancellationRequested)
			{
				return;
			}

			_logger.LogTrace("WaitAndInitClient - delay finished");

			InitClient();
		}

		public void DisposeRoomRequests()
		{
			_logger.LogTrace("DisposeRoomRequests");

			List<AbstractCommunicatorRequest> roomRequests = null;
			foreach (var request in Requests)
			{
				if (request != null && request.MessageType.StartsWith(SnipeMessageTypes.PREFIX_ROOM))
				{
					roomRequests ??= new List<AbstractCommunicatorRequest>();
					roomRequests.Add(request);
				}
			}
			if (roomRequests != null)
			{
				foreach (var request in roomRequests)
				{
					request?.Dispose();
				}
			}
		}

		public void Dispose()
		{
			_logger.LogTrace("Dispose");

			if (_client != null)
			{
				_client.ConnectionOpened -= OnClientConnectionOpened;
				_client.ConnectionClosed -= OnClientConnectionClosed;
				_client.ConnectionDisrupted -= OnClientConnectionDisrupted;
				_client.UdpConnectionFailed -= OnClientUdpConnectionFailed;
				_client.MessageReceived -= OnMessageReceived;
			}

			Disconnect();
			DisposeRequests();

			try
			{
				RaiseEvent(PreDestroy);
			}
			catch (Exception) { }

			if (_client != null)
			{
				_client.Dispose();
				_client = null;
			}
		}

		public void DisposeRequests()
		{
			_logger.LogTrace("DisposeRequests");

			if (_requests != null)
			{
				var tempRequests = _requests;
				_requests = null;
				foreach (var request in tempRequests)
				{
					request?.Dispose();
				}
			}
		}
	}
}
