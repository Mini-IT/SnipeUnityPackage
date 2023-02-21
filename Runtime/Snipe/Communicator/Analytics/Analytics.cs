using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class Analytics
	{
		public static bool IsEnabled = true;

		private static TaskScheduler _mainThreadScheduler;

		private static void InvokeInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}

		#region AnalyticsTracker

		public static TimeSpan PingTime { get; internal set; }
		public static TimeSpan ConnectionEstablishmentTime { get; internal set; }
		public static TimeSpan WebSocketTcpClientConnectionTime { get; internal set; }
		public static TimeSpan WebSocketSslAuthenticateTime { get; internal set; }
		public static TimeSpan WebSocketHandshakeTime { get; internal set; }
		public static TimeSpan WebSocketMiscTime { get; internal set; }
		public static string WebSocketDisconnectReason { get; internal set; }
		public static string ConnectionUrl { get; internal set; }
		public static Exception UdpException { get; internal set; }
		public static bool UdpDnsResolved { get; internal set; }
		public static TimeSpan UdpConnectionTime { get; internal set; }
		public static TimeSpan UdpDnsResolveTime { get; internal set; }
		public static TimeSpan UdpSocketConnectTime { get; internal set; }
		public static TimeSpan UdpSendHandshakeTime { get; internal set; }

		private static IAnalyticsTracker _tracker;
		
		private static string _userId = null;
		private static readonly object _userIdLock = new object();

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public static void SetTracker(IAnalyticsTracker tracker)
		{
			_tracker = tracker;
			
			_mainThreadScheduler = (SynchronizationContext.Current != null) ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;

			if (!string.IsNullOrEmpty(_userId))
			{
				CheckReady();
			}
		}
		
		private static bool CheckReady()
		{
			bool ready = _tracker != null && _tracker.IsInitialized && IsEnabled;
			
			if (ready)
			{
				lock (_userIdLock)
				{
					if (!string.IsNullOrEmpty(_userId))
					{
						_tracker.SetUserId(_userId);
						_userId = null;
						
						if (!string.IsNullOrEmpty(SnipeConfig.DebugId))
						{
							_tracker.SetUserProperty("debugID", SnipeConfig.DebugId);
						}
					}
				}
			}
			
			return ready;
		}
		
		public static void SetUserId(string uid)
		{
			_userId = uid;
			CheckReady();
		}

		public static void SetUserProperty(string name, string value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, int value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, float value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, double value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, bool value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty<T>(string name, IList<T> value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, IDictionary<string, object> value)
		{
			if (CheckReady())
			{
				_tracker.SetUserProperty(name, value);
			}
		}

		public static void TrackEvent(string name, IDictionary<string, object> properties = null)
		{
			if (CheckReady())
			{
				// Some trackers (for example Amplitude) may crash if used not in the main Unity thread.
				
				if (properties == null)
					properties = new Dictionary<string, object>(2);

				properties["event_type"] = name;
				properties["snipe_package_version"] = PackageInfo.VERSION;

				if (PingTime.TotalMilliseconds > 0)
					properties["ping_time"] = PingTime.TotalMilliseconds;
				
				if (SnipeCommunicator.InstanceInitialized && SnipeCommunicator.Instance.Connected)
				{	
					properties["server_reaction"] = SnipeCommunicator.Instance.ServerReaction.TotalMilliseconds;
				}

				InvokeInMainThread(() =>
				{
					_tracker.TrackEvent(EVENT_NAME, properties);
				});
			}
		}
		public static void TrackEvent(string name, string property_name, object property_value)
		{
			if (CheckReady())
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(3);
				properties[property_name] = property_value;
				TrackEvent(name, properties);
			}
		}
		public static void TrackEvent(string name, object property_value)
		{
			if (CheckReady())
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(3);
				properties["value"] = property_value;
				TrackEvent(name, properties);
			}
		}
		
		public static void TrackErrorCodeNotOk(string message_type, string error_code, SnipeObject data)
		{
			if (CheckReady() && _tracker.CheckErrorCodeTracking(message_type, error_code))
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(5);
				properties["message_type"] = message_type;
				properties["error_code"] = error_code;
				properties["data"] = data?.ToJSONString();
				TrackEvent(EVENT_ERROR_CODE_NOT_OK, properties);
			}
		}
		
		public static void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null)
		{
			if (CheckReady())
			{
				InvokeInMainThread(() =>
				{
					_tracker.TrackError(name, exception, properties);
				});
			}
		}
		
		#endregion AnalyticsTracker
		
		#region Connection events
		
		internal static void TrackSocketStartConnection(string socketName)
		{
			TrackEvent("Socket Start Connection", new Dictionary<string, object>()
			{
				["socket"] = socketName,
				["connection_url"] = Analytics.ConnectionUrl,
			});
		}
		
		#endregion Connection events
		
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