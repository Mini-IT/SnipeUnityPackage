
using System;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public class AbstractSnipeServices : ISnipeServices
	{
		public ISharedPrefs SharedPrefs => _sharedPrefs ??= CreateSharedPrefs();
		public AbstractLogService LogService => _logService ??= CreateLogService();

		private ISharedPrefs _sharedPrefs;
		private AbstractLogService _logService;

		protected virtual ISharedPrefs CreateSharedPrefs() => throw new NotImplementedException();
		protected virtual AbstractLogService CreateLogService() => throw new NotImplementedException();
	}
}
