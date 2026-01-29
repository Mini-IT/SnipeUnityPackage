using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public interface ISnipeCommunicatorAnalyticsTracker
	{
		bool IsInitialized { get; }

		void SetUserId(string uid);
		void SetUserProperty(string name, string value);
		void SetUserProperty(string name, int value);
		void SetUserProperty(string name, float value);
		void SetUserProperty(string name, double value);
		void SetUserProperty(string name, bool value);
		void SetUserProperty<T>(string name, IList<T> value);
		void SetUserProperty(string name, IDictionary<string, object> value);

		void TrackEvent(string name, IDictionary<string, object> properties = null);
		void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null);
		void TrackABEnter(string name, string variant);

		// Used for excluding some messages or error codes from analytics tracking
		bool CheckErrorCodeTracking(string messageType, string errorCode);
	}
}
