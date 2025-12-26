namespace MiniIT.Snipe
{
	public class WebSocketFactory : IWebSocketFactory
	{
		private readonly SnipeConfig _config;

		public WebSocketFactory(SnipeConfig config)
		{
			_config = config;
		}

		public WebSocketWrapper CreateWebSocket()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			return new WebSocketJSWrapper();
#else
			return _config.WebSocketImplementation switch
			{
#if BEST_WEBSOCKET
				WebSocketImplementations.BestWebSocket => new WebSocketBestWrapper(),
#endif
				_ => new WebSocketClientWrapper(),
			};
#endif
		}
	}
}
