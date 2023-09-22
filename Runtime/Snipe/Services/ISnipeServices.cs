
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public interface ISnipeServices
	{
		ISharedPrefs SharedPrefs { get; }
		AbstractLogService LogService { get; }
	}
}
