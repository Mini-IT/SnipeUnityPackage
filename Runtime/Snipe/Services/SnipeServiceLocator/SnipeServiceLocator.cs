using System;
using MiniIT.Http;
using MiniIT.Snipe.Diagnostics;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator //: ISnipeServiceLocator
	{
		public ISharedPrefs SharedPrefs => _sharedPrefs ??= _factory.CreateSharedPrefs();
		public ILogService LogService => _logService;// ??= _factory.CreateLogService();
		public ISnipeAnalyticsService Analytics => _analyticsService ??= _factory.CreateAnalyticsService();
		public IMainThreadRunner MainThreadRunner => _mainThreadRunner;
		public IApplicationInfo ApplicationInfo => _applicationInfo;
		public IStopwatchFactory FuzzyStopwatchFactory => _fuzzyStopwatchFactory ??= _factory.CreateFuzzyStopwatchFactory();
		public IHttpClientFactory HttpClientFactory => _httpClientFactory ??= _factory.CreateHttpClientFactory();
		public IDiagnosticsService Diagnostics => _diagnosticsService;


		private ISharedPrefs _sharedPrefs;
		private readonly ILogService _logService;
		private ISnipeAnalyticsService _analyticsService;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly IApplicationInfo _applicationInfo;
		private IStopwatchFactory _fuzzyStopwatchFactory;
		private IHttpClientFactory _httpClientFactory;
		private readonly IDiagnosticsService _diagnosticsService;

		private readonly ISnipeServiceLocatorFactory _factory;

		public SnipeServiceLocator(ISnipeServiceLocatorFactory factory)
		{
			_factory = factory;
			_mainThreadRunner = _factory.CreateMainThreadRunner();
			_applicationInfo = _factory.CreateApplicationInfo();
			_logService = _factory.CreateLogService();
			_diagnosticsService = new DiagnosticsService(_logService);
		}

		public void Dispose()
		{
			(_factory as IDisposable)?.Dispose();
			(_sharedPrefs as IDisposable)?.Dispose();
			(_logService as IDisposable)?.Dispose();
			(_analyticsService as IDisposable)?.Dispose();
			(_mainThreadRunner as IDisposable)?.Dispose();
			(_applicationInfo as IDisposable)?.Dispose();
			(_diagnosticsService as IDisposable)?.Dispose();
		}
	}
}
