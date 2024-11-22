using System;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public interface ISnipeServiceLocator : IDisposable
	{
		ISharedPrefs SharedPrefs { get; }
		ILogService LogService { get; }
		ISnipeAnalyticsService Analytics { get; }
		IMainThreadRunner MainThreadRunner { get; }
		IApplicationInfo ApplicationInfo { get; }
		IStopwatchFactory FuzzyStopwatchFactory { get; }
	}

	public interface ISnipeServiceLocatorFactory
	{
		ISharedPrefs CreateSharedPrefs();
		ILogService CreateLogService();
		ISnipeAnalyticsService CreateAnalyticsService();
		IMainThreadRunner CreateMainThreadRunner();
		IApplicationInfo CreateApplicationInfo();
		IStopwatchFactory CreateFuzzyStopwatchFactory();
	}
}
