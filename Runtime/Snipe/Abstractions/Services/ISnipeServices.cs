using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public interface ISnipeServices
	{
		ISharedPrefs SharedPrefs { get; }
		ILoggerFactory LoggerFactory { get; }
		ISnipeAnalyticsService Analytics { get; }
		IMainThreadRunner MainThreadRunner { get; }
		IApplicationInfo ApplicationInfo { get; }
		IStopwatchFactory FuzzyStopwatchFactory { get; }
		IHttpClientFactory HttpClientFactory { get; }
		IInternetReachability InternetReachability { get; }
		ITicker Ticker { get; }
	}
}
