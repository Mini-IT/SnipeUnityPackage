using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class SnipeCommunicator : IRoomStateListener, IDisposable
	{
		public readonly int InstanceId = new System.Random().Next();

		private const int RETRY_INIT_CLIENT_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MIN_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MAX_DELAY = 10000; // ms
		private const int RETRY_INIT_CLIENT_RANDOM_DELAY = 500; // ms

		public delegate void MessageReceivedHandler(string messageType, string errorCode, IDictionary<string, object> data, int requestId);

		/// <summary>
		/// Connection successfully establisched
		/// </summary>
		public event Action ConnectionEstablished;

		/// <summary>
		/// Connection is completely lost. No reties left
		/// </summary>
		public event Action ConnectionClosed;

		/// <summary>
		/// Connection failed or lost
		/// </summary>
		public event Action ConnectionDisrupted;

		/// <summary>
		/// Automatic connection recovery routine initiated
		/// </summary>
		public event Action ReconnectionScheduled;

		/// <summary>
		/// A message from the server is received
		/// </summary>
		public event MessageReceivedHandler MessageReceived;

		/// <summary>
		/// Disposal routine is initiated
		/// </summary>
		public event Action PreDestroy;

		public string ConnectionId => Client?.ConnectionId;
		public TimeSpan CurrentRequestElapsed => Client?.CurrentRequestElapsed ?? TimeSpan.Zero;

		internal SnipeClient Client { get; private set; }

		public bool AllowRequestsToWaitForLogin { get; set; } = true;

		public int RestoreConnectionAttempts = 3;
		private int _restoreConnectionAttempt;

		private List<AbstractCommunicatorRequest> _requests;
		public List<AbstractCommunicatorRequest> Requests => _requests ??= new List<AbstractCommunicatorRequest>();

		public readonly HashSet<SnipeRequestDescriptor> MergeableRequestTypes = new HashSet<SnipeRequestDescriptor>();

		public bool Connected => Client != null && Client.Connected;
		public bool LoggedIn => Client != null && Client.LoggedIn;

		public bool? RoomJoined => (Client == null) ? null : (Client.LoggedIn && _roomStateObserver.State == RoomState.Joined);

		public bool BatchMode
		{
			get => Client?.BatchMode ?? false;
			set
			{
				if (Client != null)
				{
					Client.BatchMode = value;
				}
			}
		}

		private bool _disconnecting = false;

		private readonly object _clientLock = new object();

		private CancellationTokenSource _delayedInitCancellation;

		private readonly SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly RoomStateObserver _roomStateObserver;
		private readonly ILogger _logger;

		public SnipeCommunicator(int contextId, SnipeConfig config)
		{
			_config = config;

			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_analytics = SnipeServices.Analytics.GetTracker(contextId);
			_logger = SnipeServices.LogService.GetLogger(nameof(SnipeCommunicator));

			_roomStateObserver = new RoomStateObserver(this);

			_logger.LogTrace($"PACKAGE VERSION: {PackageInfo.VERSION_NAME}");
		}

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void Start()
		{
			//_mainThreadRunner.SetMainThread();

			InitClient();
		}

		private void InitClient()
		{
			if (_delayedInitCancellation != null)
			{
				_delayedInitCancellation.Cancel();
				_delayedInitCancellation = null;
			}

			if (LoggedIn)
			{
				_logger.LogWarning($"({InstanceId}) InitClient - already logged in");
				return;
			}

			if (Client == null)
			{
				Client = new SnipeClient(_config);
				Client.ConnectionOpened += OnClientConnectionOpened;
				Client.ConnectionClosed += OnClientConnectionClosed;
				Client.UdpConnectionFailed += OnClientUdpConnectionFailed;
				Client.MessageReceived += OnMessageReceived;
			}

			lock (_clientLock)
			{
				if (!Client.Connected)
				{
					_disconnecting = false;
					Client.Connect();

					var transportInfo = Client.GetTransportInfo();

					_mainThreadRunner.RunInMainThread(() =>
					{
						AnalyticsTrackStartConnection(transportInfo);
					});
				}
			}
		}

		public void Disconnect()
		{
			_logger.LogTrace($"({InstanceId}) Disconnect");

			_roomStateObserver.Reset();
			_disconnecting = true;

			if (_delayedInitCancellation != null)
			{
				_delayedInitCancellation.Cancel();
				_delayedInitCancellation = null;
			}

			if (Client != null && Client.Connected)
			{
				Client.Disconnect();
			}
		}

		private void OnClientConnectionOpened()
		{
			_logger.LogTrace($"({InstanceId}) Client connection opened");

			_restoreConnectionAttempt = 0;
			_disconnecting = false;
			_analytics.ConnectionEventsEnabled = true;

			var transportInfo = Client.GetTransportInfo();

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded(transportInfo);
				ConnectionEstablished?.Invoke();
			});
		}

		private void OnClientConnectionClosed()
		{
			_logger.LogTrace($"({InstanceId}) [{Client?.ConnectionId}] Client connection closed");

			_roomStateObserver.Reset();

			var transportInfo = Client?.GetTransportInfo() ?? default;

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed(transportInfo);
				OnConnectionFailed();
			});
		}

		private void OnClientUdpConnectionFailed()
		{
			var transportInfo = Client?.GetTransportInfo() ?? default;

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackUdpConnectionFailed(transportInfo);
			});
		}

		// Main thread
		private void OnConnectionFailed()
		{
			ConnectionDisrupted?.Invoke();

			if (_restoreConnectionAttempt < RestoreConnectionAttempts && !_disconnecting)
			{
				ReconnectionScheduled?.Invoke();

				AttemptToRestoreConnection();
				return;
			}

			ConnectionClosed?.Invoke();

			DisposeRequests();
		}

		private void AttemptToRestoreConnection()
		{
			_restoreConnectionAttempt++;
			_logger.LogTrace($"({InstanceId}) Attempt to restore connection {_restoreConnectionAttempt}");

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

		#region Safe events raising

		private SafeEventRaiser _safeEventRaiser;

		private void RaiseEvent(Delegate eventDelegate, params object[] args)
		{
			_safeEventRaiser ??= new SafeEventRaiser((handler, e) =>
			{
				_logger.LogError($"({InstanceId}) RaiseEvent - Error in the handler {handler?.Method?.Name}: {e}");
			});

			_safeEventRaiser.RaiseEvent(eventDelegate, args);
		}

		#endregion

		private async void DelayedInitClient(CancellationToken cancellation)
		{
			_logger.LogTrace($"({InstanceId}) WaitAndInitClient - start delay");
			
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

			_logger.LogTrace($"({InstanceId}) WaitAndInitClient - delay finished");
			
			InitClient();
		}

		public void DisposeRoomRequests()
		{
			_logger.LogTrace($"({InstanceId}) DisposeRoomRequests");

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
			_logger.LogTrace($"({InstanceId}) Dispose");

			if (Client != null)
			{
				Client.ConnectionOpened -= OnClientConnectionOpened;
				Client.ConnectionClosed -= OnClientConnectionClosed;
				Client.UdpConnectionFailed -= OnClientUdpConnectionFailed;
				Client.MessageReceived -= OnMessageReceived;
			}

			Disconnect();
			DisposeRequests();

			try
			{
				RaiseEvent(PreDestroy);
			}
			catch (Exception) { }

			if (Client != null)
			{
				Client.Dispose();
				Client = null;
			}
		}

		public void DisposeRequests()
		{
			_logger.LogTrace($"({InstanceId}) DisposeRequests");

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

		#region IRoomStateListener

		void IRoomStateListener.OnRoomJoined()
		{
			// TODO: Increase HttpTransport heardbeat rate
		}

		void IRoomStateListener.OnRoomLeft()
		{
			DisposeRoomRequests();
		}

		#endregion

		#region Analytics

		private void AnalyticsTrackStartConnection(TransportInfo transportInfo)
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			var properties = new Dictionary<string, object>(2);
			FillTransportInfo(properties, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_START_CONNECTION, properties);
		}

		private void AnalyticsTrackConnectionSucceeded(TransportInfo transportInfo)
		{
			var data = Client.UdpClientConnected ? new Dictionary<string, object>()
			{
				["connection_type"] = "udp",
				["connection_time"] = _analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,
			} :
			new Dictionary<string, object>()
			{
				["connection_type"] = "websocket",
				["connection_time"] = _analytics.ConnectionEstablishmentTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,

				["ws tcp client connection"] = _analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds,
				["ws ssl auth"] = _analytics.WebSocketSslAuthenticateTime.TotalMilliseconds,
				["ws upgrade request"] = _analytics.WebSocketHandshakeTime.TotalMilliseconds,
				["ws misc"] = _analytics.ConnectionEstablishmentTime.TotalMilliseconds <= 0 ? 0:
					_analytics.ConnectionEstablishmentTime.TotalMilliseconds -
					_analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds -
					_analytics.WebSocketSslAuthenticateTime.TotalMilliseconds -
					_analytics.WebSocketHandshakeTime.TotalMilliseconds,
			};

			FillTransportInfo(data, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_CONNECTED, data);
		}

		private void AnalyticsTrackConnectionFailed(TransportInfo transportInfo)
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			var properties = new Dictionary<string, object>()
			{
				//["communicator"] = this.name,
				["connection_id"] = Client?.ConnectionId,
				//["disconnect_reason"] = Client?.DisconnectReason,
				//["check_connection_message"] = Client?.CheckConnectionMessageType,
				["connection_url"] = _analytics.ConnectionUrl,
				["ws tcp client connection"] = _analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds,
				["ws ssl auth"] = _analytics.WebSocketSslAuthenticateTime.TotalMilliseconds,
				["ws upgrade request"] = _analytics.WebSocketHandshakeTime.TotalMilliseconds,
				["ws disconnect reason"] = _analytics.WebSocketDisconnectReason,
			};
			FillTransportInfo(properties, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_DISCONNECTED, properties);
		}

		private void AnalyticsTrackUdpConnectionFailed(TransportInfo transportInfo)
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			var properties = new Dictionary<string, object>()
			{
				["connection_type"] = "udp",
				["connection_time"] = _analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,
			};
			FillTransportInfo(properties, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_DISCONNECTED + " UDP", properties);
		}

		private static void FillTransportInfo(IDictionary<string, object> properties, TransportInfo transportInfo)
		{
			properties["transport_protocol"] = transportInfo.Protocol.ToString();
			properties["transport_implementation"] = transportInfo.ClientImplementation;
		}

		#endregion Analytics
	}
}
