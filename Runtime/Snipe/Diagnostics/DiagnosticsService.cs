using System.Collections.Generic;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public class DiagnosticsService : IDiagnosticsService
	{
		private readonly ILogService _logService;

		private readonly Dictionary<string, DiagnosticsChannel> _channels = new();

		public DiagnosticsService(ILogService logService)
		{
			_logService = logService;
		}

		public IDiagnosticsChannel GetChannel(string name)
		{
			if (_channels.TryGetValue(name, out var descriptor))
			{
				return descriptor;
			}

			var logger = _logService.GetLogger(name);
			var channel = new DiagnosticsChannel(logger);

			_channels[name] = channel;

			return channel;
		}
	}
}
