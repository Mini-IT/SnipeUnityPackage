using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public interface ISnipeServiceLocatorFactory
	{
		ISharedPrefs CreateSharedPrefs();
		ILogService CreateLogService();
		IMainThreadRunner CreateMainThreadRunner();
		IApplicationInfo CreateApplicationInfo();
		IStopwatchFactory CreateFuzzyStopwatchFactory();
		IHttpClientFactory CreateHttpClientFactory();
		IInternetReachabilityProvider CreateInternetReachabilityProvider();
		ITicker CreateTicker();
	}
}
