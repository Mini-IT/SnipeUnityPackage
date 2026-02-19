using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Logging.Unity;
using MiniIT.Snipe.Debugging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe.Unity
{
	public class UnitySnipeServicesFactory : ISnipeInfrastructureProvider, ISnipeErrorsTrackerProvider
	{
		public static ISnipeErrorsTracker DebugErrorsTracker { get; set; } = null;

		ISnipeErrorsTracker ISnipeErrorsTrackerProvider.ErrorsTracker => DebugErrorsTracker;

		public virtual ISharedPrefs GetSharedPrefs() => SharedPrefs.Instance;

		public virtual ILoggerFactory GetLoggerFactory() => LoggerFactory.Create(builder =>
		{
			builder.SetMinimumLevel(LogLevel.Trace);
			builder.AddUnityLogger(options =>
			{
				options.MinimumLogLevelProvider = new TraceMinimumLogLevelProvider();
				options.StackTraceMapper = new ScriptOnlyStackTraceMapper();
			});
		});

		public virtual IMainThreadRunner GetMainThreadRunner() => new MainThreadRunner();
		public virtual IApplicationInfo GetApplicationInfo() => new UnityApplicationInfo();
		public virtual IStopwatchFactory GetFuzzyStopwatchFactory() => new FuzzyStopwatchFactory();
		public virtual IHttpClientFactory GetHttpClientFactory() => new DefaultHttpClientFactory();
		public virtual IInternetReachability GetInternetReachability() => new UnityInternetReachability();
		public ITicker GetTicker() => new UnityUpdateTicker();
	}
}
