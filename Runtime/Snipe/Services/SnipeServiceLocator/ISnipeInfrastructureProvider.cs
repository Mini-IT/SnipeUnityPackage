using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public interface ISnipeInfrastructureProvider
	{
		ISharedPrefs GetSharedPrefs();
		ILoggerFactory GetLoggerFactory();
		IMainThreadRunner GetMainThreadRunner();
		IApplicationInfo GetApplicationInfo();
		IStopwatchFactory GetFuzzyStopwatchFactory();
		IHttpClientFactory GetHttpClientFactory();
		IInternetReachability GetInternetReachability();
		ITicker GetTicker();
	}
}
