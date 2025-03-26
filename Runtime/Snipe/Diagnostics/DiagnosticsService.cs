using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public interface IDiagnosticsChannel : ILogger, IDisposable
	{
	}

	public readonly struct DiagnosticsMessage
	{
		public EventId EventId { get; }
		public LogLevel LogLevel { get; }
		public Exception Exception { get; }
		public string Message { get; }

		public DiagnosticsMessage(EventId eventId, LogLevel logLevel, Exception exception, string message)
		{
			EventId = eventId;
			LogLevel = logLevel;
			Exception = exception;
			Message = message;
		}
	}

	public class DiagnosticsScope : IDisposable
	{
		public EventId EventId { get; }
		public IList<DiagnosticsMessage> Messages { get; }

		public DiagnosticsScope(EventId eventId)
		{
			EventId = eventId;
			Messages = new List<DiagnosticsMessage>();
		}

		public void Dispose()
		{
			Messages.Clear();
		}
	}

	public class DiagnosticsChannel : IDiagnosticsChannel
	{
		private readonly ILogger _logger;
		private readonly List<DiagnosticsScope> _scopes;

		private DiagnosticsScope _currentScope;

		public DiagnosticsChannel(ILogger logger)
		{
			_logger = logger;
			_currentScope = new DiagnosticsScope(new EventId(0));
			_scopes = new List<DiagnosticsScope>()
			{
				_currentScope
			};
		}

		#region ILogger

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			string message = formatter(state, exception);
			_currentScope.Messages.Add(new DiagnosticsMessage(eventId, logLevel, exception, message));

			_logger.Log(logLevel, eventId, state, exception, formatter);
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return _logger.IsEnabled(logLevel);
		}

		public IDisposable BeginScope<TState>(TState state) where TState : notnull
		{
			string name = state?.ToString();
			_currentScope = new DiagnosticsScope(new EventId(_currentScope.EventId.Id, name));
			return _currentScope; //_logger.BeginScope(state);
		}

		#endregion

		public void Dispose()
		{
			_currentScope = null;
			foreach (var scope in _scopes)
			{
				scope.Dispose();
			}
			_scopes.Clear();
		}
	}

	public interface IDiagnosticsService
	{
		IDiagnosticsChannel GetChannel(string name);
	}

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
