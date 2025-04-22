using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public partial class SnipeCommunicator
	{
		public TimeSpan CurrentRequestElapsed => _client?.CurrentRequestElapsed ?? TimeSpan.Zero;

		private void AnalyticsTrackStartConnection(TransportInfo transportInfo)
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			var properties = new Dictionary<string, object>(2);
			FillTransportInfo(properties, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_START_CONNECTION, properties);
		}

		private void AnalyticsTrackConnectionSucceeded(TransportInfo transportInfo)
		{
			var data = _client.UdpClientConnected ? new Dictionary<string, object>()
			{
				["connection_type"] = "udp",
				["connection_time"] = _analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,
			} :
			new Dictionary<string, object>()
			{
				["connection_type"] = "websocket",
				["connection_time"] = _analytics.ConnectionEstablishmentTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,

				["ws tcp client connection"] = _analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds,
				["ws ssl auth"] = _analytics.WebSocketSslAuthenticateTime.TotalMilliseconds,
				["ws upgrade request"] = _analytics.WebSocketHandshakeTime.TotalMilliseconds,
				["ws misc"] = _analytics.ConnectionEstablishmentTime.TotalMilliseconds <= 0 ? 0:
					_analytics.ConnectionEstablishmentTime.TotalMilliseconds -
					_analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds -
					_analytics.WebSocketSslAuthenticateTime.TotalMilliseconds -
					_analytics.WebSocketHandshakeTime.TotalMilliseconds,
			};

			FillTransportInfo(data, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_CONNECTED, data);
		}

		private void AnalyticsTrackConnectionFailed(TransportInfo transportInfo)
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			var properties = new Dictionary<string, object>()
			{
				//["communicator"] = this.name,
				["connection_id"] = _client?.ConnectionId,
				//["disconnect_reason"] = Client?.DisconnectReason,
				//["check_connection_message"] = Client?.CheckConnectionMessageType,
				["connection_url"] = _analytics.ConnectionUrl,
				["ws tcp client connection"] = _analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds,
				["ws ssl auth"] = _analytics.WebSocketSslAuthenticateTime.TotalMilliseconds,
				["ws upgrade request"] = _analytics.WebSocketHandshakeTime.TotalMilliseconds,
				["ws disconnect reason"] = _analytics.WebSocketDisconnectReason,
			};
			FillTransportInfo(properties, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_DISCONNECTED, properties);
		}

		private void AnalyticsTrackUdpConnectionFailed(TransportInfo transportInfo)
		{
			if (!_analytics.ConnectionEventsEnabled)
				return;

			var properties = new Dictionary<string, object>()
			{
				["connection_type"] = "udp",
				["connection_time"] = _analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = _analytics.ConnectionUrl,
			};
			FillTransportInfo(properties, transportInfo);

			_analytics.TrackEvent(SnipeAnalyticsTracker.EVENT_COMMUNICATOR_DISCONNECTED + " UDP", properties);
		}

		private static void FillTransportInfo(IDictionary<string, object> properties, TransportInfo transportInfo)
		{
			properties["transport_protocol"] = transportInfo.Protocol.ToString();
			properties["transport_implementation"] = transportInfo.ClientImplementation;
		}
	}
}
