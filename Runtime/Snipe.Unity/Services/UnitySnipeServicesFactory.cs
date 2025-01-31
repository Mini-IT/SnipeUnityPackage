using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.Logging.Unity;
using MiniIT.Storage;

namespace MiniIT.Snipe.Unity
{
	public class UnitySnipeServicesFactory : ISnipeServiceLocatorFactory
	{
		public static Debugging.ISnipeErrorsTracker DebugErrorsTracker { get; set; } = null;

		public virtual ISharedPrefs CreateSharedPrefs() => SharedPrefs.Instance;

		public virtual ILogService CreateLogService() => new UnityLogService();
		public virtual ISnipeAnalyticsService CreateAnalyticsService() => new SnipeAnalyticsService(DebugErrorsTracker);
		public virtual IMainThreadRunner CreateMainThreadRunner() => new MainThreadRunner();
		public virtual IApplicationInfo CreateApplicationInfo() => new UnityApplicationInfo();
		public virtual IStopwatchFactory CreateFuzzyStopwatchFactory() => new FuzzyStopwatchFactory();
		public virtual IHttpClientFactory CreateHttpClientFactory() => new DefaultHttpClientFactory();
	}
}
