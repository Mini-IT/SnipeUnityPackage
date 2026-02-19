using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	internal sealed class TransportFactory
	{
		private readonly SnipeOptions _options;
		private readonly IAnalyticsContext _analytics;
		private readonly ISnipeServices _services;

		internal TransportFactory(SnipeOptions options, IAnalyticsContext analytics, ISnipeServices services)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			_options = options;
			_analytics = analytics;
			_services = services;
		}

		internal KcpTransport CreateKcpTransport(Action<Transport> onConnectionOpened, Action<Transport> onConnectionClosed,
			Action<IDictionary<string, object>> onMessageReceived)
		{
			return new KcpTransport(new TransportOptions()
			{
				SnipeOptions = _options,
				AnalyticsContext = _analytics,
				SnipeServices = _services,
				ConnectionOpenedHandler = (t) =>
				{
					_analytics.UdpConnectionTime = StopwatchUtil.GetElapsedTime(Stopwatch.GetTimestamp());
					onConnectionOpened(t);
				},
				ConnectionClosedHandler = onConnectionClosed,
				MessageReceivedHandler = onMessageReceived
			});
		}

		internal WebSocketTransport CreateWebSocketTransport(Action<Transport> onConnectionOpened, Action<Transport> onConnectionClosed,
			Action<IDictionary<string, object>> onMessageReceived)
		{
			return new WebSocketTransport(new TransportOptions()
			{
				SnipeOptions = _options,
				AnalyticsContext = _analytics,
				SnipeServices = _services,
				ConnectionOpenedHandler = onConnectionOpened,
				ConnectionClosedHandler = onConnectionClosed,
				MessageReceivedHandler = onMessageReceived
			});
		}

		internal HttpTransport CreateHttpTransport(Action<Transport> onConnectionOpened, Action<Transport> onConnectionClosed,
			Action<IDictionary<string, object>> onMessageReceived)
		{
			return new HttpTransport(new TransportOptions()
			{
				SnipeOptions = _options,
				AnalyticsContext = _analytics,
				SnipeServices = _services,
				ConnectionOpenedHandler = onConnectionOpened,
				ConnectionClosedHandler = onConnectionClosed,
				MessageReceivedHandler = onMessageReceived
			});
		}
	}
}
