using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class Analytics : IDisposable
	{
		#region static

		public static bool IsEnabled { get; set; } = true;

		private static void RunInMainThread(Action action)
		{
			SnipeServices.Instance.MainThreadRunner.RunInMainThread(action);
		}

		private static Dictionary<string, Analytics> s_instances;
		private static readonly object s_lock = new object();

		public static Analytics GetInstance(string contextId = null)
		{
			contextId ??= string.Empty;
			Analytics instance;
			lock (s_lock)
			{
				s_instances ??= new Dictionary<string, Analytics>();
				if (!s_instances.TryGetValue(contextId, out instance))
				{
					instance = new Analytics(contextId);
					s_instances[contextId] = instance;
				}
			}
			return instance;
		}

		private static ISnipeCommunicatorAnalyticsTracker s_tracker;

		#endregion

		#region AnalyticsTracker

		public TimeSpan PingTime { get; internal set; }
		public TimeSpan ServerReaction { get; internal set; }
		public TimeSpan ConnectionEstablishmentTime { get; internal set; }
		public TimeSpan WebSocketTcpClientConnectionTime { get; internal set; }
		public TimeSpan WebSocketSslAuthenticateTime { get; internal set; }
		public TimeSpan WebSocketHandshakeTime { get; internal set; }
		public TimeSpan WebSocketMiscTime { get; internal set; }
		public string WebSocketDisconnectReason { get; internal set; }
		public string ConnectionUrl { get; internal set; }
		public TimeSpan UdpConnectionTime { get; internal set; }

		public bool ConnectionEventsEnabled { get; internal set; } = true;

		private string _userId = null;
		private string _debugId = null;
		private readonly object _userIdLock = new object();
		private readonly string _contextId;

		public Analytics(string contextId) => _contextId = contextId;
		~Analytics() => Dispose();

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public static void SetTracker(ISnipeCommunicatorAnalyticsTracker tracker)
		{
			s_tracker = tracker;

			if (s_instances != null)
			{
				foreach (var instance in s_instances.Values)
				{
					if (!string.IsNullOrEmpty(instance?._userId))
					{
						instance.CheckReady();
					}
				}
			}
		}
		
		private bool CheckReady()
		{
			bool ready = s_tracker != null && s_tracker.IsInitialized && IsEnabled;
			
			if (ready)
			{
				lock (_userIdLock)
				{
					if (!string.IsNullOrEmpty(_userId))
					{
						if (string.IsNullOrEmpty(_contextId)) // Default context only
						{
							s_tracker.SetUserId(_userId);
						}
						_userId = null;
					}

					if (!string.IsNullOrEmpty(_debugId))
					{
						string prefix = string.IsNullOrEmpty(_contextId) ? "" : $"{_contextId} ";
						s_tracker.SetUserProperty(prefix + "debugID", _debugId);
						_debugId = null;
					}
				}
			}
			
			return ready;
		}

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
				s_tracker.SetUserProperty(name, value);
			}
		}
		public void SetUserProperty(string name, int value)
		{
			if (CheckReady())
			{
				s_tracker.SetUserProperty(name, value);
			}
		}
		public void SetUserProperty(string name, float value)
		{
			if (CheckReady())
			{
				s_tracker.SetUserProperty(name, value);
			}
		}
		public void SetUserProperty(string name, double value)
		{
			if (CheckReady())
			{
				s_tracker.SetUserProperty(name, value);
			}
		}
		public void SetUserProperty(string name, bool value)
		{
			if (CheckReady())
			{
				s_tracker.SetUserProperty(name, value);
			}
		}
		public void SetUserProperty<T>(string name, IList<T> value)
		{
			if (CheckReady())
			{
				s_tracker.SetUserProperty(name, value);
			}
		}
		public void SetUserProperty(string name, IDictionary<string, object> value)
		{
			if (CheckReady())
			{
				s_tracker.SetUserProperty(name, value);
			}
		}

		public void TrackEvent(string name, IDictionary<string, object> properties = null)
		{
			if (CheckReady())
			{
				properties ??= new Dictionary<string, object>();
				properties["event_type"] = name;
				properties["snipe_package_version"] = PackageInfo.VERSION;

				if (!string.IsNullOrEmpty(_contextId))
					properties["sinpe_context"] = _contextId;

				if (PingTime.TotalMilliseconds > 0)
					properties["ping_time"] = PingTime.TotalMilliseconds;
				if (ServerReaction.TotalMilliseconds > 0)
					properties["server_reaction"] = ServerReaction.TotalMilliseconds;

				// Some trackers (for example Amplitude) may crash if used not in the main Unity thread.
				RunInMainThread(() =>
				{
					s_tracker.TrackEvent(EVENT_NAME, properties);
				});
			}
		}
		public void TrackEvent(string name, string property_name, object property_value)
		{
			if (CheckReady())
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(3);
				properties[property_name] = property_value;
				TrackEvent(name, properties);
			}
		}
		public void TrackEvent(string name, object property_value)
		{
			if (CheckReady())
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(3);
				properties["value"] = property_value;
				TrackEvent(name, properties);
			}
		}
		
		public void TrackErrorCodeNotOk(string message_type, string error_code, SnipeObject data)
		{
			if (CheckReady() && s_tracker.CheckErrorCodeTracking(message_type, error_code))
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(5);
				properties["message_type"] = message_type;
				properties["error_code"] = error_code;
				properties["data"] = data?.ToJSONString();
				TrackEvent(EVENT_ERROR_CODE_NOT_OK, properties);
			}
		}
		
		public void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null)
		{
			if (CheckReady())
			{
				if (!string.IsNullOrEmpty(_contextId))
				{
					properties ??= new Dictionary<string, object>();
					properties["sinpe_context"] = _contextId;
				}

				RunInMainThread(() =>
				{
					s_tracker.TrackError(name, exception, properties);
				});
			}
		}
		
		#endregion AnalyticsTracker
		
		public void Dispose()
		{
			if (s_instances == null)
				return;

			lock (s_lock)
			{
				foreach (var pair in s_instances)
				{
					if (pair.Value == this)
					{
						s_instances.Remove(pair.Key);
						break;
					}
				}
			}
		}

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
