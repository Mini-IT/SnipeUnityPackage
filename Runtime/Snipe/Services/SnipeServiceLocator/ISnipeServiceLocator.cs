using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public interface ISnipeServiceLocator
	{
		ISharedPrefs SharedPrefs { get; }
		ILogService LogService { get; }
		IMainThreadRunner MainThreadRunner { get; }
	}

	public interface ISnipeServiceLocatorFactory
	{
		ISharedPrefs CreateSharedPrefs();
		ILogService CreateLogService();
		IMainThreadRunner CreateMainThreadRunner();
	}
}
