using MiniIT.Snipe.Logging;
using MiniIT.Snipe.Logging.Unity;
using MiniIT.Snipe.SharedPrefs;
using MiniIT.Snipe.SharedPrefs.Unity;

namespace MiniIT.Snipe.Unity
{
	public class UnitySnipeServicesFactory : ISnipeServiceLocatorFactory
	{
#if UNITY_EDITOR
		public static Debugging.ISnipeErrorsTracker DebugErrorsTracker { get; set; }
#endif

		public virtual ISharedPrefs CreateSharedPrefs() =>
#if UNITY_WEBGL && !UNITY_EDITOR
			new WebGLSharedPrefs();
#else
			new UnitySharedPrefs();
#endif

		public virtual ILogService CreateLogService() => new UnityLogService();

		public virtual ISnipeAnalyticsService CreateAnalyticsService() =>
#if UNITY_EDITOR
			new SnipeAnalyticsService(DebugErrorsTracker);
#else
			new SnipeAnalyticsService();
#endif

		public virtual IMainThreadRunner CreateMainThreadRunner() => new MainThreadRunner();
		public virtual IApplicationInfo CreateApplicationInfo() => new UnityApplicationInfo();
		public virtual IStopwatchFactory CreateFuzzyStopwatchFactory() => new FuzzyStopwatchFactory();
	}
}
