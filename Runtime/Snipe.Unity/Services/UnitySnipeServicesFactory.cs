using MiniIT.Http;
using MiniIT.Snipe.Debugging;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.Logging.Unity;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe.Unity
{
	public class UnitySnipeServicesFactory : ISnipeServiceLocatorFactory, ISnipeErrorsTrackerProvider
	{
		public static ISnipeErrorsTracker DebugErrorsTracker { get; set; } = null;

		ISnipeErrorsTracker ISnipeErrorsTrackerProvider.ErrorsTracker => DebugErrorsTracker;

		public virtual ISharedPrefs CreateSharedPrefs() => SharedPrefs.Instance;

		public virtual ILogService CreateLogService() => new UnityLogService();
		public virtual IMainThreadRunner CreateMainThreadRunner() => new MainThreadRunner();
		public virtual IApplicationInfo CreateApplicationInfo() => new UnityApplicationInfo();
		public virtual IStopwatchFactory CreateFuzzyStopwatchFactory() => new FuzzyStopwatchFactory();
		public virtual IHttpClientFactory CreateHttpClientFactory() => new DefaultHttpClientFactory();
		public virtual IInternetReachabilityProvider CreateInternetReachabilityProvider() => new UnityInternetReachabilityProvider();
		public ITicker CreateTicker() => new UnityUpdateTicker();
	}
}
