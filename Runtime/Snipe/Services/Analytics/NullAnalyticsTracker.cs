using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public sealed class NullAnalyticsTracker : ISnipeCommunicatorAnalyticsTracker
	{
		public bool IsInitialized => true;

		public NullAnalyticsTracker() {}

		public void SetUserId(string uid) {}

		public void SetUserProperty(string name, string value) {}

		public void SetUserProperty(string name, int value) {}

		public void SetUserProperty(string name, float value) {}

		public void SetUserProperty(string name, double value) {}

		public void SetUserProperty(string name, bool value) {}

		public void SetUserProperty<T>(string name, IList<T> value) {}

		public void SetUserProperty(string name, IDictionary<string, object> value) {}

		public void TrackEvent(string name, IDictionary<string, object> properties = null) {}

		public void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null) {}

		public void TrackABEnter(string name, string variant) {}

		public bool CheckErrorCodeTracking(string messageType, string errorCode) => true;
	}
}
