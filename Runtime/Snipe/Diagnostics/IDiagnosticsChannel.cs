using System;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public interface IDiagnosticsChannel : ILogger, IDisposable
	{
	}
}
