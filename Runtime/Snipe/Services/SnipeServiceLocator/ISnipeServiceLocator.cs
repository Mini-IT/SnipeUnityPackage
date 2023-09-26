using System;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public interface ISnipeServiceLocator : IDisposable
	{
		ISharedPrefs SharedPrefs { get; }
		ILogService LogService { get; }
		SnipeAnalyticsService Analytics { get; }
		IMainThreadRunner MainThreadRunner { get; }
	}

	public interface ISnipeServiceLocatorFactory
	{
		ISharedPrefs CreateSharedPrefs();
		ILogService CreateLogService();
		SnipeAnalyticsService CreateAnalyticsService();
		IMainThreadRunner CreateMainThreadRunner();
	}
}
