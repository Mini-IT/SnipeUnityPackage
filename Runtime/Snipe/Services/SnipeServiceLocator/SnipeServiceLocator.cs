// ReSharper disable SuspiciousTypeConversion.Global

using System;
using MiniIT.Http;
using MiniIT.Snipe.Debugging;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator : ISnipeServices, IDisposable
	{
		public ISharedPrefs SharedPrefs { get; }
		public ILogService LogService { get; }
		public ISnipeAnalyticsService Analytics { get; }
		public IMainThreadRunner MainThreadRunner { get; }
		public IApplicationInfo ApplicationInfo { get; }
		public IStopwatchFactory FuzzyStopwatchFactory { get; }
		public IHttpClientFactory HttpClientFactory { get; }
		public IInternetReachabilityProvider InternetReachabilityProvider { get; }
		public ITicker Ticker { get; }

		public SnipeServiceLocator(ISnipeServiceLocatorFactory factory)
		{
			SharedPrefs = factory.CreateSharedPrefs();
			LogService = factory.CreateLogService();
			MainThreadRunner = factory.CreateMainThreadRunner();

			Func<ISnipeErrorsTracker> errorsTrackerGetter = null;
			if (factory is ISnipeErrorsTrackerProvider errorsTrackerProvider)
			{
				errorsTrackerGetter = () => errorsTrackerProvider.ErrorsTracker;
			}
			Analytics = new SnipeAnalyticsService(MainThreadRunner, errorsTrackerGetter);

			FuzzyStopwatchFactory = factory.CreateFuzzyStopwatchFactory();
			HttpClientFactory = factory.CreateHttpClientFactory();
			ApplicationInfo = factory.CreateApplicationInfo();
			InternetReachabilityProvider = factory.CreateInternetReachabilityProvider();
			Ticker = factory.CreateTicker();
		}

		public void Dispose()
		{
			(SharedPrefs as IDisposable)?.Dispose();
			(LogService as IDisposable)?.Dispose();
			(Analytics as IDisposable)?.Dispose();
			(MainThreadRunner as IDisposable)?.Dispose();
			(ApplicationInfo as IDisposable)?.Dispose();
			(InternetReachabilityProvider as IDisposable)?.Dispose();
			(Ticker as IDisposable)?.Dispose();
		}
	}
}
