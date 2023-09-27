using MiniIT.Snipe.Logging;
using MiniIT.Snipe.Logging.Unity;
using MiniIT.Snipe.SharedPrefs;
using MiniIT.Snipe.SharedPrefs.Unity;

namespace MiniIT.Snipe.Unity
{
	public class UnitySnipeServicesFactory : ISnipeServiceLocatorFactory
	{
		public ISharedPrefs CreateSharedPrefs() => new UnitySharedPrefs();
		public ILogService CreateLogService() => new UnityLogService();
		public ISnipeAnalyticsService CreateAnalyticsService() => new SnipeAnalyticsService();
		public IMainThreadRunner CreateMainThreadRunner() => new MainThreadRunner();
	}
}
