
namespace MiniIT.Http
{
	public static class HttpClientFactory
	{
		public static IHttpClient Create()
		{
#if UNITY_WEBGL
			return new UnityHttpClient();
#else
			return new SystemHttpClient();
#endif
		}
	}
}
