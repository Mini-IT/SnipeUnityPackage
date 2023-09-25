using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator : ISnipeServiceLocator
	{
		public ISharedPrefs SharedPrefs => _sharedPrefs ??= _factory.CreateSharedPrefs();
		public ILogService LogService => _logService ??= _factory.CreateLogService();
		public IMainThreadRunner MainThreadRunner { get; }

		private ISharedPrefs _sharedPrefs;
		private ILogService _logService;

		private readonly ISnipeServiceLocatorFactory _factory;

		public SnipeServiceLocator(ISnipeServiceLocatorFactory factory)
		{
			_factory = factory;
			MainThreadRunner = _factory.CreateMainThreadRunner();
		}
	}
}
