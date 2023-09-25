using System;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public class SnipeServiceLocator : ISnipeServiceLocator
	{
		public ISharedPrefs SharedPrefs => _sharedPrefs ??= _factory.CreateSharedPrefs();
		public ILogService LogService => _logService ??= _factory.CreateLogService();
		public IMainThreadRunner MainThreadRunner => _mainThreadRunner;

		private ISharedPrefs _sharedPrefs;
		private ILogService _logService;
		private readonly IMainThreadRunner _mainThreadRunner;

		private readonly ISnipeServiceLocatorFactory _factory;

		public SnipeServiceLocator(ISnipeServiceLocatorFactory factory)
		{
			_factory = factory;
			_mainThreadRunner = _factory.CreateMainThreadRunner();
		}

		public void Dispose()
		{
			(_sharedPrefs as IDisposable)?.Dispose();
			(_logService as IDisposable)?.Dispose();
			(_mainThreadRunner as IDisposable)?.Dispose();
		}
	}
}
