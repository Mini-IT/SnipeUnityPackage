using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe
{
	public class WebSocketFactory : IWebSocketFactory
	{
		private readonly SnipeConfig _config;
		private readonly ISnipeServices _services;

		public WebSocketFactory(SnipeConfig config, ISnipeServices services)
		{
			if (services == null)
			{
				throw new System.ArgumentNullException(nameof(services));
			}

			_config = config;
			_services = services;
		}

		public WebSocketWrapper CreateWebSocket()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			return new WebSocketJSWrapper(_services);
#else
			return _config.WebSocketImplementation switch
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
