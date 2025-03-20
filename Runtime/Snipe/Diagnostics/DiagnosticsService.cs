using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public interface IDiagnosticsChannel : ILogger
	{

	}

	public class DiagnosticsChannel : IDiagnosticsChannel
	{
		private readonly ILogger _logger;

		public DiagnosticsChannel(ILogger logger)
		{
			_logger = logger;
		}

		#region ILogger

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			_logger.Log(logLevel, eventId, state, exception, formatter);
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return _logger.IsEnabled(logLevel);
		}

		public IDisposable BeginScope<TState>(TState state) where TState : notnull
		{
			return _logger.BeginScope(state);
		}

		#endregion
	}

	public interface IDiagnosticsService
	{
		DiagnosticsChannel GetChannel(string name);
	}

	public class DiagnosticsService : IDiagnosticsService
	{
		private readonly ILogService _logService;

		private readonly Dictionary<string, DiagnosticsChannel> _channels = new();

		public DiagnosticsService(ILogService logService)
		{
			_logService = logService;
		}

		public DiagnosticsChannel GetChannel(string name)
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
