using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public interface ISnipeServices
	{
		ISharedPrefs SharedPrefs { get; }
		ILogService LogService { get; }
		ISnipeAnalyticsService Analytics { get; }
		IMainThreadRunner MainThreadRunner { get; }
		IApplicationInfo ApplicationInfo { get; }
		IStopwatchFactory FuzzyStopwatchFactory { get; }
		IHttpClientFactory HttpClientFactory { get; }
		IInternetReachabilityProvider InternetReachabilityProvider { get; }
		ITicker Ticker { get; }
	}
}
