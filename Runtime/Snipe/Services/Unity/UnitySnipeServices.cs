
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.Logging.Unity;
using MiniIT.Snipe.SharedPrefs;
using MiniIT.Snipe.SharedPrefs.Unity;

namespace MiniIT.Snipe
{
	public class UnitySnipeServices : AbstractSnipeServices
	{
		protected override ISharedPrefs CreateSharedPrefs() => new UnitySharedPrefs();
		protected override AbstractLogService CreateLogService() => new UnityLogService();
	}
}
