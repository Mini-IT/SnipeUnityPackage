using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Logging.Unity;
using MiniIT.Snipe.Debugging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe.Unity
{
	public class UnitySnipeServicesFactory : ISnipeServiceLocatorFactory, ISnipeErrorsTrackerProvider
	{
		public static ISnipeErrorsTracker DebugErrorsTracker { get; set; } = null;

		ISnipeErrorsTracker ISnipeErrorsTrackerProvider.ErrorsTracker => DebugErrorsTracker;

		public virtual ISharedPrefs CreateSharedPrefs() => SharedPrefs.Instance;

		public virtual ILoggerFactory CreateLoggerFactory() => LoggerFactory.Create(builder =>
		{
			builder.SetMinimumLevel(LogLevel.Trace);
			builder.AddUnityLogger(options =>
			{
				options.MinimumLogLevelProvider = new TraceMinimumLogLevelProvider();
				options.StackTraceMapper = new ScriptOnlyStackTraceMapper();
			});
		});

		public virtual IMainThreadRunner CreateMainThreadRunner() => new MainThreadRunner();
		public virtual IApplicationInfo CreateApplicationInfo() => new UnityApplicationInfo();
		public virtual IStopwatchFactory CreateFuzzyStopwatchFactory() => new FuzzyStopwatchFactory();
		public virtual IHttpClientFactory CreateHttpClientFactory() => new DefaultHttpClientFactory();
		public virtual IInternetReachabilityProvider CreateInternetReachabilityProvider() => new UnityInternetReachabilityProvider();
		public ITicker CreateTicker() => new UnityUpdateTicker();
	}
}
