
namespace MiniIT.Http
{
	public static class HttpClientFactory
	{
		public static IHttpClient Create()
		{
#if BEST_HTTP
			return new BestHttpClient();
#elif UNITY_WEBGL
			return new UnityHttpClient();
#else
			return new SystemHttpClient();
#endif
		}
	}
}
