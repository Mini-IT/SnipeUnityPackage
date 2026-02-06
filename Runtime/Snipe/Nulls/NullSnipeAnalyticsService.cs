using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public sealed class NullSnipeAnalyticsService : ISnipeAnalyticsService
	{
		public bool IsEnabled
		{
			get => false;
			set { }
		}

		public ISnipeAnalyticsTracker GetTracker(int contextId = 0) => NullSnipeAnalyticsTracker.Instance;

		public void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker)
		{
		}
	}

	public sealed class NullSnipeAnalyticsTracker : ISnipeAnalyticsTracker
	{
		public static readonly NullSnipeAnalyticsTracker Instance = new NullSnipeAnalyticsTracker();

		public bool ConnectionEventsEnabled { get; set; }
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

		public void SetDebugId(string id) { }
		public void SetUserId(string uid) { }
		public void SetUserProperty(string name, string value) { }
		public void SetUserProperty(string name, int value) { }
		public void SetUserProperty(string name, float value) { }
		public void SetUserProperty(string name, double value) { }
		public void SetUserProperty(string name, bool value) { }
		public void SetUserProperty<T>(string name, IList<T> value) { }
		public void SetUserProperty(string name, IDictionary<string, object> value) { }
		public void TrackEvent(string name, IDictionary<string, object> properties = null) { }
		public void TrackEvent(string name, string propertyName, object propertyValue) { }
		public void TrackEvent(string name, object propertyValue) { }
		public void TrackErrorCodeNotOk(string messageType, string errorCode, IDictionary<string, object> data) { }
		public void TrackError(string name, Exception exception = null, IDictionary<string, object> properties = null) { }
		public void TrackABEnter(string name, string variant) { }
		public void TrackSnipeConfigLoadingStats(SnipeConfigLoadingStatistics statistics) { }
	}
}
