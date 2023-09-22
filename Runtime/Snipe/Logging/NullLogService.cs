using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MiniIT.Snipe.Logging
{
	public class NullLogService : AbstractLogService
	{
		protected override ILoggerFactory CreateLoggerFactory() => new NullLoggerFactory();
	}
}
