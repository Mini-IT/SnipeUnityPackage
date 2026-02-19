using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public interface ISnipeServiceLocatorFactory
	{
		ISharedPrefs CreateSharedPrefs();
		ILoggerFactory CreateLoggerFactory();
		IMainThreadRunner CreateMainThreadRunner();
		IApplicationInfo CreateApplicationInfo();
		IStopwatchFactory CreateFuzzyStopwatchFactory();
		IHttpClientFactory CreateHttpClientFactory();
		IInternetReachabilityProvider CreateInternetReachabilityProvider();
		ITicker CreateTicker();
	}
}
