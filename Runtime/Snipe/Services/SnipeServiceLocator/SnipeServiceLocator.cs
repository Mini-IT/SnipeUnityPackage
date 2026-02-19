// ReSharper disable SuspiciousTypeConversion.Global

using System;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Snipe.Debugging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator : ISnipeServices, IDisposable
	{
		public ISharedPrefs SharedPrefs { get; }
		public ILoggerFactory LoggerFactory { get; }
		public ISnipeAnalyticsService Analytics { get; }
		public IMainThreadRunner MainThreadRunner { get; }
		public IApplicationInfo ApplicationInfo { get; }
		public IStopwatchFactory FuzzyStopwatchFactory { get; }
		public IHttpClientFactory HttpClientFactory { get; }
		public IInternetReachability InternetReachability { get; }
		public ITicker Ticker { get; }

		public SnipeServiceLocator(ISnipeInfrastructureProvider provider)
		{
			SharedPrefs = provider.GetSharedPrefs();
			LoggerFactory = provider.GetLoggerFactory();
			MainThreadRunner = provider.GetMainThreadRunner();

			Func<ISnipeErrorsTracker> errorsTrackerGetter = null;
			if (provider is ISnipeErrorsTrackerProvider errorsTrackerProvider)
			{
				errorsTrackerGetter = () => errorsTrackerProvider.ErrorsTracker;
			}
			Analytics = new SnipeAnalyticsService(MainThreadRunner, errorsTrackerGetter);

			FuzzyStopwatchFactory = provider.GetFuzzyStopwatchFactory();
			HttpClientFactory = provider.GetHttpClientFactory();
			ApplicationInfo = provider.GetApplicationInfo();
			InternetReachability = provider.GetInternetReachability();
			Ticker = provider.GetTicker();
		}

		public void Dispose()
		{
			(SharedPrefs as IDisposable)?.Dispose();
			LoggerFactory?.Dispose();
			(Analytics as IDisposable)?.Dispose();
			(MainThreadRunner as IDisposable)?.Dispose();
			(ApplicationInfo as IDisposable)?.Dispose();
			(InternetReachability as IDisposable)?.Dispose();
			(Ticker as IDisposable)?.Dispose();
		}
	}
}
