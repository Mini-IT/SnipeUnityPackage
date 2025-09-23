using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	internal sealed class TransportFactory
	{
		private readonly SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;

		internal TransportFactory(SnipeConfig config, SnipeAnalyticsTracker analytics)
		{
			_config = config;
			_analytics = analytics;
		}

		internal KcpTransport CreateKcpTransport(Action<Transport> onConnectionOpened, Action<Transport> onConnectionClosed,
			Action<IDictionary<string, object>> onMessageReceived)
		{
			var transport = new KcpTransport(_config, _analytics);

			transport.ConnectionOpenedHandler = (t) =>
			{
				_analytics.UdpConnectionTime = StopwatchUtil.GetElapsedTime(Stopwatch.GetTimestamp());
				onConnectionOpened(t);
			};

			transport.ConnectionClosedHandler = onConnectionClosed;
			transport.MessageReceivedHandler = onMessageReceived;

			return transport;
		}

		internal WebSocketTransport CreateWebSocketTransport(Action<Transport> onConnectionOpened, Action<Transport> onConnectionClosed,
			Action<IDictionary<string, object>> onMessageReceived)
		{
			return new WebSocketTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = onConnectionOpened,
				ConnectionClosedHandler = onConnectionClosed,
				MessageReceivedHandler = onMessageReceived
			};
		}

		internal HttpTransport CreateHttpTransport(Action<Transport> onConnectionOpened, Action<Transport> onConnectionClosed,
			Action<IDictionary<string, object>> onMessageReceived)
		{
			return new HttpTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = onConnectionOpened,
				ConnectionClosedHandler = onConnectionClosed,
				MessageReceivedHandler = onMessageReceived
			};
		}
	}
}
