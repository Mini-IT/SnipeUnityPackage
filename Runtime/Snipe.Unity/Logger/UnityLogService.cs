using Microsoft.Extensions.Logging;
using MiniIT.Logging.Unity;

namespace MiniIT.Snipe.Logging.Unity
{
	public class UnityLogService : AbstractLogService
	{
		protected override ILoggerFactory CreateLoggerFactory() => UnityLoggerFactory.Default;
	}
}
