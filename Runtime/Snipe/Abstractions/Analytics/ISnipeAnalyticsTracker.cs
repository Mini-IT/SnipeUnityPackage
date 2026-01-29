using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public interface ISnipeAnalyticsTracker
	{
		bool ConnectionEventsEnabled { get; set; }
		TimeSpan PingTime { get; set; }
		TimeSpan ServerReaction { get; set; }
		TimeSpan ConnectionEstablishmentTime { get; set; }
		TimeSpan WebSocketTcpClientConnectionTime { get; set; }
		TimeSpan WebSocketSslAuthenticateTime { get; set; }
		TimeSpan WebSocketHandshakeTime { get; set; }
		TimeSpan WebSocketMiscTime { get; set; }
		string WebSocketDisconnectReason { get; set; }
		string ConnectionUrl { get; set; }
		TimeSpan UdpConnectionTime { get; set; }

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
	}
}
