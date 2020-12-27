using System.Collections;
using System.Collections.Generic;
using MiniIT;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class Analytics : MonoBehaviour
	{
		public static bool IsEnabled = true;
		
		#region AnalyticsTracker
		
		private static IAnalyticsTracker mTracker;
		
		private static string mUserId = null;
		
		public static void SetTracker(IAnalyticsTracker tracker)
		{
			mTracker = tracker;
			
			if (!string.IsNullOrEmpty(mUserId))
			{
				CheckReady();
			}
		}
		
		private static bool CheckReady()
		{
			bool ready = mTracker != null && mTracker.IsInitialized && IsEnabled;
			
			if (ready && !string.IsNullOrEmpty(mUserId))
			{
				mTracker.SetUserId(mUserId);
				mUserId = null;
			}
			
			return ready;
		}
		
		public static void SetUserId(string uid)
		{
			if (CheckReady())
			{
				mTracker.SetUserId(uid);
				mUserId = null;
			}
			else
			{
				mUserId = uid;
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
				// Some trackers (for example Amplitude) may crash if used not in the main Unity thread.
				// We'll put events into a queue and call mTracker.TrackEvent in MonoBehaviour.Update method.
				
				GetInstance().EnqueueEvent(name, properties);
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
		
		public static void TrackErrorCodeNotOk(string message_type, string error_code, ExpandoObject data)
		{
			if (CheckReady() && mTracker.CheckErrorCodeTracking(message_type, error_code))
			{
				Dictionary<string, object> properties = new Dictionary<string, object>(1);
				properties["message_type"] = message_type;
				properties["error_code"] = error_code;
				properties["data"] = data.ToJSONString();
				TrackEvent(EVENT_ERROR_CODE_NOT_OK, properties);
			}
		}
		
		#endregion AnalyticsTracker
		
		#region Constants
		
		public const string EVENT_COMMUNICATOR_CONNECTED = "Snipe - Communicator Connected";
		public const string EVENT_COMMUNICATOR_DISCONNECTED = "Snipe - Communicator Disconnected";
		public const string EVENT_ROOM_COMMUNICATOR_CONNECTED = "Snipe - Room Communicator Connected";
		public const string EVENT_ROOM_COMMUNICATOR_DISCONNECTED = "Snipe - Room Communicator Disconnected";
		public const string EVENT_ACCOUNT_REGISTERED = "Snipe - Account registered";
		public const string EVENT_ACCOUNT_REGISTERATION_FAILED = "Snipe - Account registeration failed";
		public const string EVENT_LOGIN_REQUEST_SENT = "Snipe - Login request sent";
		public const string EVENT_LOGIN_RESPONSE_RECEIVED = "Snipe - Login response received";
		public const string EVENT_AUTH_LOGIN_REQUEST_SENT = "Snipe - Auth Login request sent";
		public const string EVENT_AUTH_LOGIN_RESPONSE_RECEIVED = "Snipe - Auth Login response received";
		public const string EVENT_SINGLE_REQUEST_CLIENT_CONNECTED = "Snipe - SingleRequestClient Connected";
		public const string EVENT_SINGLE_REQUEST_CLIENT_DISCONNECTED = "Snipe - SingleRequestClient Disconnected";
		public const string EVENT_SINGLE_REQUEST_RESPONSE = "Snipe - SingleRequestClient Response";
		
		private const string EVENT_ERROR_CODE_NOT_OK = "Snipe - ErrorCode not ok";
		
		#endregion Constants
		
		#region MonoBehaviour
		
		private static Analytics mInstance;
		private static Analytics GetInstance()
		{
			if (mInstance == null)
			{
				mInstance = new GameObject("SnipeAnalyticsTracker").AddComponent<Analytics>();
			}
			return mInstance;
		}
		
		private void Awake()
		{
			if (mInstance != null && mInstance != this)
			{
				Destroy(this.gameObject);
				return;
			}
			
			DontDestroyOnLoad(this.gameObject);
		}
		
		#region EventsQueue
		
		class EventsQueueItem
		{
			internal string name;
			internal IDictionary<string, object> properties;
		}
		
		private List<EventsQueueItem> mEventsQueue;
		
		private void EnqueueEvent(string name, IDictionary<string, object> properties = null)
		{
			if (mEventsQueue == null)
				mEventsQueue = new List<EventsQueueItem>();
			lock (mEventsQueue)
			{
				mEventsQueue.Add(new EventsQueueItem() { name = name, properties = properties });
			}
		}
		
		private void Update()
		{
			if (mEventsQueue != null && mEventsQueue.Count > 0)
			{
				lock (mEventsQueue)
				{
					foreach (var item in mEventsQueue)
					{
						mTracker.TrackEvent(item.name, item.properties);
					}
					mEventsQueue.Clear();
				}
			}
		}
		
		#endregion EventsQueue
		
		#endregion MonoBehaviour
	}
}