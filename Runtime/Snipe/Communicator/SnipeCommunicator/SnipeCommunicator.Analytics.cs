using System;

namespace MiniIT.Snipe
{
	public partial class SnipeCommunicator
	{
		public TimeSpan CurrentRequestElapsed => _client?.CurrentRequestElapsed ?? TimeSpan.Zero;

		private void AnalyticsTrackStartConnection(TransportInfo transportInfo)
		{
			_analytics.TrackConnectionStarted(transportInfo);
		}

		private void AnalyticsTrackConnectionSucceeded(TransportInfo transportInfo)
		{
			_analytics.TrackConnectionSucceeded(_client.UdpClientConnected, transportInfo);
		}

		private void AnalyticsTrackConnectionFailed(TransportInfo transportInfo)
		{
			_analytics.TrackConnectionFailed(_client?.ConnectionId, transportInfo);
		}

		private void AnalyticsTrackUdpConnectionFailed(TransportInfo transportInfo)
		{
			_analytics.TrackUdpConnectionFailed(transportInfo);
		}
	}
}
