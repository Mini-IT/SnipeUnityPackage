﻿using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class SnipeCommunicator : IDisposable
	{
		public readonly int InstanceId = new System.Random().Next();

		private const int RETRY_INIT_CLIENT_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MIN_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MAX_DELAY = 10000; // ms
		private const int RETRY_INIT_CLIENT_RANDOM_DELAY = 500; // ms

		public delegate void MessageReceivedHandler(string messageType, string errorCode, SnipeObject data, int requestId);

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

		public bool AllowRequestsToWaitForLogin = true;

		public int RestoreConnectionAttempts = 3;
		private int _restoreConnectionAttempt;

		private List<AbstractCommunicatorRequest> _requests;
		public List<AbstractCommunicatorRequest> Requests => _requests ??= new List<AbstractCommunicatorRequest>();

		public readonly HashSet<SnipeRequestDescriptor> MergeableRequestTypes = new HashSet<SnipeRequestDescriptor>();

		public bool Connected => Client != null && Client.Connected;

		public bool LoggedIn => Client != null && Client.LoggedIn;

		private bool? _roomJoined = null;
		public bool? RoomJoined => (Client != null && Client.LoggedIn) ? _roomJoined : null;

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
		private readonly ILogger _logger;

		public SnipeCommunicator(SnipeConfig config)
		{
			_config = config;

			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_analytics = SnipeServices.Analytics.GetTracker(config.ContextId);
			_logger = SnipeServices.LogService.GetLogger(nameof(SnipeCommunicator));

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

					_mainThreadRunner.RunInMainThread(() =>
					{
						AnalyticsTrackStartConnection();
					});
				}
			}
		}

		public void Disconnect()
		{
			_logger.LogTrace($"({InstanceId}) Disconnect");

			_roomJoined = null;
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

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded();
				ConnectionEstablished?.Invoke();
			});
		}

		private void OnClientConnectionClosed()
		{
			_logger.LogTrace($"({InstanceId}) [{Client?.ConnectionId}] Client connection closed");

			_roomJoined = null;

			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed();
				OnConnectionFailed();
			});
		}

		private void OnClientUdpConnectionFailed()
		{
			_mainThreadRunner.RunInMainThread(() =>
			{
				AnalyticsTrackUdpConnectionFailed();
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

		private void OnMessageReceived(string message_type, string error_code, SnipeObject data, int request_id)
		{
			// _logger.LogTrace($"({INSTANCE_ID}) [{Client?.ConnectionId}] OnMessageReceived {request_id} {message_type} {error_code} " + (data != null ? data.ToJSONString() : "null"));

			//if (message_type == SnipeMessageTypes.USER_LOGIN) // handled in AuthSubsystem
			//{
			//}
			//else
			if (message_type == SnipeMessageTypes.ROOM_JOIN)
			{
				if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_IN_ROOM)
				{
					_roomJoined = true;
				}
				else
				{
					_roomJoined = false;
					DisposeRoomRequests();
				}
			}
			else if (message_type == SnipeMessageTypes.ROOM_DEAD)
			{
				_roomJoined = false;
				DisposeRoomRequests();
			}

			if (MessageReceived != null)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					RaiseEvent(MessageReceived, message_type, error_code, data, request_id);
				});
			}

			if (error_code != SnipeErrorCodes.OK)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					_analytics.TrackErrorCodeNotOk(message_type, error_code, data);
				});
			}
		}

		#region Safe events raising

		private SafeEventRaiser _safeEventRaiser;

		private void RaiseEvent(Delegate eventDelegate, params object[] args)
		{
			_safeEventRaiser ??= new SafeEventRaiser((handler, e) =>
			{
				_logger.LogTrace($"({InstanceId}) RaiseEvent - Error in the handler {handler?.Method?.Name}: {e}");
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

			List<AbstractCommunicatorRequest> room_requests = null;
			foreach (var request in Requests)
			{
				if (request != null && request.MessageType.StartsWith(SnipeMessageTypes.PREFIX_ROOM))
				{
					room_requests ??= new List<AbstractCommunicatorRequest>();
					room_requests.Add(request);
				}
			}
			if (room_requests != null)
			{
				foreach (var request in room_requests)
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
				var temp_requests = _requests;
				_requests = null;
				foreach (var request in temp_requests)
				{
					request?.Dispose();
				}
			}
		}

		#region Analytics

		private void AnalyticsTrackStartConnection()
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_START_CONNECTION);
		}

		private void AnalyticsTrackConnectionSucceeded()
		{
			var data = Client.UdpClientConnected ? new SnipeObject()
			{
				["connection_type"] = "udp",
				["connection_time"] = _analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,
			} :
			new SnipeObject()
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

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_CONNECTED, data);
		}

		private void AnalyticsTrackConnectionFailed()
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_DISCONNECTED, new SnipeObject()
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
			});
		}

		private void AnalyticsTrackUdpConnectionFailed()
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_DISCONNECTED + " UDP", new SnipeObject()
			{
				["connection_type"] = "udp",
				["connection_time"] = _analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,
			});
		}

		#endregion Analytics
	}
}
