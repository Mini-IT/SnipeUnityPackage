using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe
{
	public class WebSocketFactory : IWebSocketFactory
	{
		private readonly SnipeOptions _options;
		private readonly ISnipeServices _services;

		public WebSocketFactory(SnipeOptions options, ISnipeServices services)
		{
			if (services == null)
			{
				throw new System.ArgumentNullException(nameof(services));
			}

			_options = options;
			_services = services;
		}

		public WebSocketWrapper CreateWebSocket()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			return new WebSocketJSWrapper(_services);
#else
			return _options.WebSocketImplementation switch
			{
#if BEST_WEBSOCKET
				WebSocketImplementations.BestWebSocket => new WebSocketBestWrapper(_services),
#endif
				_ => new WebSocketClientWrapper(_services),
			};
#endif
		}
	}
}
