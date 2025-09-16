using System;
using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator : ISnipeServiceLocator
	{
		public ISharedPrefs SharedPrefs => _sharedPrefs ??= _factory.CreateSharedPrefs();
		public ILogService LogService => _logService ??= _factory.CreateLogService();
		public ISnipeAnalyticsService Analytics => _analyticsService ??= _factory.CreateAnalyticsService();
		public IMainThreadRunner MainThreadRunner => _mainThreadRunner;
		public IApplicationInfo ApplicationInfo => _applicationInfo;
		public IStopwatchFactory FuzzyStopwatchFactory => _fuzzyStopwatchFactory ??= _factory.CreateFuzzyStopwatchFactory();
		public IHttpClientFactory HttpClientFactory => _httpClientFactory ??= _factory.CreateHttpClientFactory();
		public IInternetReachabilityProvider InternetReachabilityProvider => _internetReachabilityProvider;

		private ISharedPrefs _sharedPrefs;
		private ILogService _logService;
		private ISnipeAnalyticsService _analyticsService;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly IApplicationInfo _applicationInfo;
		private readonly IInternetReachabilityProvider _internetReachabilityProvider;
		private IStopwatchFactory _fuzzyStopwatchFactory;
		private IHttpClientFactory _httpClientFactory;

		private readonly ISnipeServiceLocatorFactory _factory;

		public SnipeServiceLocator(ISnipeServiceLocatorFactory factory)
		{
			_factory = factory;
			_mainThreadRunner = _factory.CreateMainThreadRunner();
			_applicationInfo = _factory.CreateApplicationInfo();
			_internetReachabilityProvider = _factory.CreateInternetReachabilityProvider();
		}

		public void Dispose()
		{
			(_sharedPrefs as IDisposable)?.Dispose();
			(_logService as IDisposable)?.Dispose();
			(_analyticsService as IDisposable)?.Dispose();
			(_mainThreadRunner as IDisposable)?.Dispose();
			(_applicationInfo as IDisposable)?.Dispose();
			(_internetReachabilityProvider as IDisposable)?.Dispose();
		}
	}
}
