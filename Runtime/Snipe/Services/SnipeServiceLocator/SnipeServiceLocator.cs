// ReSharper disable SuspiciousTypeConversion.Global

using System;
using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator : ISnipeServiceLocator
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
			Analytics = factory.CreateAnalyticsService();
			MainThreadRunner = factory.CreateMainThreadRunner();
			if (Analytics is SnipeAnalyticsService analyticsService)
			{
				analyticsService.SetMainThreadRunner(MainThreadRunner);
			}
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
