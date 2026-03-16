
namespace MiniIT.Http
{
	public class DefaultHttpClientFactory : IHttpClientFactory
	{
		public IHttpClient CreateHttpClient()
		{
#if BEST_HTTP
			return new BestHttpClient();
#else
			return new UnityHttpClient();
#endif
		}
	}
}
