using System.Collections;
using System.Collections.Generic;
using MiniIT;

namespace MiniIT.Snipe
{
	public static class Analytics
	{
		public static bool IsEnabled = true;
		
		#region AnalyticsTracker
		
		private static IAnalyticsTracker mTracker;
		
		public static void SetTracker(IAnalyticsTracker tracker)
		{
			mTracker = tracker;
		}
		
		private static bool CheckReady()
		{
			return mTracker != null && mTracker.IsInitialized && IsEnabled;
		}
		
		public static void SetUserId(string uid)
		{
			if (CheckReady())
			{
				mTracker.SetUserId(uid);
			}
		}

		public static void SetUserProperty(string name, string value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, int value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, float value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, double value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, bool value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty<T>(string name, IList<T> value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}
		public static void SetUserProperty(string name, IDictionary<string, object> value)
		{
			if (CheckReady())
			{
				mTracker.SetUserProperty(name, value);
			}
		}

		public static void TrackEvent(string name, IDictionary<string, object> properties = null)
		{
			if (CheckReady())
			{
				mTracker.TrackEvent(name, properties);
			}
		}
		public static void TrackEvent(string name, string property_name, object property_value)
		{
			if (CheckReady())
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(1);
				properties[property_name] = property_value;
				TrackEvent(name, properties);
			}
		}
		public static void TrackEvent(string name, object property_value)
		{
			if (CheckReady())
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(1);
				properties["value"] = property_value;
				TrackEvent(name, properties);
			}
		}
		
		#endregion AnalyticsTracker
		
		#region Constants
		
		public const string EVENT_COMMUNICATOR_CONNECTED = "Snipe - Communicator Connected";
		public const string EVENT_COMMUNICATOR_DISCONNECTED = "Snipe - Communicator Disconnected";
		public const string EVENT_ROOM_COMMUNICATOR_CONNECTED = "Snipe - Room Communicator Connected";
		public const string EVENT_ROOM_COMMUNICATOR_DISCONNECTED = "Snipe - Room Communicator Disconnected";
		public const string EVENT_ACCOUNT_REGISTERED = "Snipe - auth/user.register";
		public const string EVENT_LOGIN_REQUEST_SENT = "Snipe - Login request sent";
		public const string EVENT_LOGIN_RESPONSE_RECEIVED = "Snipe - Login response received";
		public const string EVENT_AUTH_LOGIN_REQUEST_SENT = "Snipe - Auth Login request sent";
		public const string EVENT_AUTH_LOGIN_RESPONSE_RECEIVED = "Snipe - Auth Login response received";
		public const string EVENT_SINGLE_REQUEST_CLIENT_CONNECTED = "Snipe - SingleRequestClient Connected";
		public const string EVENT_SINGLE_REQUEST_CLIENT_DISCONNECTED = "Snipe - SingleRequestClient Disconnected";
		public const string EVENT_SINGLE_REQUEST_RESPONSE = "Snipe - SingleRequestClient Response";
		
		#endregion Constants
		
		
		// public static void OnSnipeAccountReister(string error_code, int user_id = 0)
		// {
			// TrackEvent(EVENT_ACCOUNT_REGISTERED, new ExpandoObject()
			// {
				// ["error_code"] = error_code,
				// ["user_id"] = user_id,
			// });
		// }
	}
}