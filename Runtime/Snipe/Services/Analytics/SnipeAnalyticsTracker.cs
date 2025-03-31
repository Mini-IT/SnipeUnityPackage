using System;
using System.Collections.Generic;
using MiniIT.Snipe.Debugging;

namespace MiniIT.Snipe
{
	public class SnipeAnalyticsTracker
	{
		public bool IsEnabled => _analyticsService.IsEnabled;

		#region Analytics Properties

		public TimeSpan PingTime { get; set; }
		public TimeSpan ServerReaction { get; set; }
		public TimeSpan ConnectionEstablishmentTime { get; set; }
		public TimeSpan WebSocketTcpClientConnectionTime { get; set; }
		public TimeSpan WebSocketSslAuthenticateTime { get; set; }
		public TimeSpan WebSocketHandshakeTime { get; set; }
		public TimeSpan WebSocketMiscTime { get; set; }
		public string WebSocketDisconnectReason { get; set; }
		public string ConnectionUrl { get; set; }
		public TimeSpan UdpConnectionTime { get; set; }

		public bool ConnectionEventsEnabled { get; set; } = true;

		#endregion

		private ISnipeCommunicatorAnalyticsTracker _externalTracker;
		private readonly ISnipeErrorsTracker _errorsTracker;

		private string _userId = null;
		private string _debugId = null;
		private readonly object _userIdLock = new object();
		private readonly int _contextId;
		private readonly SnipeAnalyticsService _analyticsService;
		private readonly IMainThreadRunner _mainThreadRunner;

		internal SnipeAnalyticsTracker(SnipeAnalyticsService analyticsService, int contextId, ISnipeErrorsTracker errorsTracker)
		{
			_analyticsService = analyticsService;
			_contextId = contextId;
			_errorsTracker = errorsTracker;
			_mainThreadRunner = SnipeServices.MainThreadRunner;
		}

		internal void SetExternalTracker(ISnipeCommunicatorAnalyticsTracker externalTracker)
		{
			_externalTracker = externalTracker;
			if (_externalTracker != null)
			{
				CheckReady();
			}
		}

		private bool CheckReady()
		{
			bool ready = _externalTracker != null && _externalTracker.IsInitialized && IsEnabled;

			if (ready)
			{
				lock (_userIdLock)
				{
					if (!string.IsNullOrEmpty(_userId))
					{
						if (_contextId == 0) // Default context only
						{
							_externalTracker.SetUserId(_userId);
						}
						_userId = null;
					}

					if (!string.IsNullOrEmpty(_debugId))
					{
						string prefix = (_contextId == 0) ? "" : $"{_contextId} ";
						_externalTracker.SetUserProperty(prefix + "debugID", _debugId);
						_debugId = null;
					}
				}
			}
			return ready;
		}

		#region Analytics methods

		public void SetDebugId(string id)
		{
			_debugId = id;
			CheckReady();
		}

		public void SetUserId(string uid)
		{
			_userId = uid;
			CheckReady();
		}

		public void SetUserProperty(string name, string value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void SetUserProperty(string name, int value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void SetUserProperty(string name, float value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void SetUserProperty(string name, double value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void SetUserProperty(string name, bool value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void SetUserProperty<T>(string name, IList<T> value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void SetUserProperty(string name, IDictionary<string, object> value)
		{
			if (CheckReady())
			{
				_externalTracker.SetUserProperty(name, value);
			}
		}

		public void TrackEvent(string name, IDictionary<string, object> properties = null)
		{
			if (CheckReady())
			{
				properties ??= new Dictionary<string, object>();
				properties["event_type"] = name;
				properties["snipe_package_version"] = PackageInfo.VERSION_NAME;

				if (_contextId != 0)
					properties["sinpe_context"] = _contextId;

				if (PingTime.TotalMilliseconds > 0)
					properties["ping_time"] = PingTime.TotalMilliseconds;
				if (ServerReaction.TotalMilliseconds > 0)
					properties["server_reaction"] = ServerReaction.TotalMilliseconds;

				// Some trackers (for example Amplitude) may crash if used not in the main Unity thread.
				_mainThreadRunner.RunInMainThread(() =>
				{
					_externalTracker.TrackEvent(EVENT_NAME, properties);
				});
			}
		}

		public void TrackEvent(string name, string propertyName, object propertyValue)
		{
			if (CheckReady())
			{
				var properties = new Dictionary<string, object>(3)
				{
					[propertyName] = propertyValue
				};
				TrackEvent(name, properties);
			}
		}

		public void TrackEvent(string name, object propertyValue)
		{
			if (CheckReady())
			{
				var properties = new Dictionary<string, object>(3)
				{
					["value"] = propertyValue
				};
				TrackEvent(name, properties);
			}
		}

		public void TrackErrorCodeNotOk(string messageType, string errorCode, IDictionary<string, object> data)
		{
			if (!CheckReady() || !_externalTracker.CheckErrorCodeTracking(messageType, errorCode))
			{
				return;
			}

			var properties = new Dictionary<string, object>(5)
			{
				["message_type"] = messageType,
				["error_code"] = errorCode,
				["data"] = data != null ? fastJSON.JSON.ToJSON(data) : null,
			};
			TrackEvent(EVENT_ERROR_CODE_NOT_OK, properties);

			_errorsTracker?.TrackNotOk(properties);
		}

		public void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null)
		{
			if (!CheckReady())
			{
				return;
			}

			if (_contextId != 0)
			{
				properties ??= new Dictionary<string, object>();
				properties["sinpe_context"] = _contextId;
			}

			_mainThreadRunner.RunInMainThread(() =>
			{
				_externalTracker.TrackError(name, exception, properties);
			});
		}

		public void TrackABEnter(string name, string variant)
		{
			if (CheckReady())
			{
				_externalTracker.TrackABEnter(name, variant);
			}
		}

		#endregion

		#region Constants

		private const string EVENT_NAME = "Snipe Event";
		public const string EVENT_COMMUNICATOR_START_CONNECTION = "Communicator Start Connection";
		public const string EVENT_COMMUNICATOR_CONNECTED = "Communicator Connected";
		public const string EVENT_COMMUNICATOR_DISCONNECTED = "Communicator Disconnected";
		public const string EVENT_ROOM_COMMUNICATOR_CONNECTED = "Room Communicator Connected";
		public const string EVENT_ROOM_COMMUNICATOR_DISCONNECTED = "Room Communicator Disconnected";
		public const string EVENT_ACCOUNT_REGISTERED = "Account registered";
		public const string EVENT_ACCOUNT_REGISTERATION_FAILED = "Account registeration failed";
		public const string EVENT_LOGIN_REQUEST_SENT = "Login request sent";
		public const string EVENT_LOGIN_RESPONSE_RECEIVED = "Login response received";
		public const string EVENT_AUTH_LOGIN_REQUEST_SENT = "Auth Login request sent";
		public const string EVENT_AUTH_LOGIN_RESPONSE_RECEIVED = "Auth Login response received";
		public const string EVENT_SINGLE_REQUEST_CLIENT_CONNECTED = "SingleRequestClient Connected";
		public const string EVENT_SINGLE_REQUEST_CLIENT_DISCONNECTED = "SingleRequestClient Disconnected";
		public const string EVENT_SINGLE_REQUEST_RESPONSE = "SingleRequestClient Response";

		private const string EVENT_ERROR_CODE_NOT_OK = "ErrorCode not ok";

		#endregion Constants

	}
}
