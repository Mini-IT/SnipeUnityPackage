
namespace MiniIT.Http
{
	public class DefaultHttpClientFactory : IHttpClientFactory
	{
		public IHttpClient CreateHttpClient()
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
