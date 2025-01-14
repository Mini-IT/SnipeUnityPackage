
namespace MiniIT.Http
{
	public interface IHttpClientFactory
	{
		IHttpClient CreateHttpClient();
	}

	public class DefaultHttpClientFactory : IHttpClientFactory
	{
		public static IHttpClientFactory Instance { get; } = new DefaultHttpClientFactory();

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
