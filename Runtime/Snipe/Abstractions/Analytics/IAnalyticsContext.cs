using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public interface IAnalyticsContext
	{
		bool ConnectionEventsEnabled { set; }
		TimeSpan PingTime { set; }
		TimeSpan ServerReaction { set; }
		TimeSpan ConnectionEstablishmentTime { set; }
		string ConnectionUrl { set; }
		TimeSpan UdpConnectionTime { set; }

		void SetDebugId(string id);
		void SetUserId(string uid);
		void SetUserProperty(string name, string value);
		void SetUserProperty(string name, int value);
		void SetUserProperty(string name, float value);
		void SetUserProperty(string name, double value);
		void SetUserProperty(string name, bool value);
		void SetUserProperty<T>(string name, IList<T> value);
		void SetUserProperty(string name, IDictionary<string, object> value);
		void TrackEvent(string name, IDictionary<string, object> properties = null);
		void TrackEvent(string name, string propertyName, object propertyValue);
		void TrackEvent(string name, object propertyValue);
		void TrackErrorCodeNotOk(string messageType, string errorCode, IDictionary<string, object> data);
		void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null);
		void TrackABEnter(string name, string variant);
		void TrackSnipeConfigLoadingStats(SnipeConfigLoadingStatistics statistics);

		void TrackConnectionStarted(TransportInfo transportInfo);
		void TrackConnectionSucceeded(bool udpConnected, TransportInfo transportInfo);
		void TrackConnectionFailed(string connectionId, TransportInfo transportInfo);
		void TrackUdpConnectionFailed(TransportInfo transportInfo);
	}
}
