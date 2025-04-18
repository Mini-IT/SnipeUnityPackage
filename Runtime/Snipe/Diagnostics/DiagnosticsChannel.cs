using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public class DiagnosticsChannel : IDiagnosticsChannel
	{
		private readonly ILogger _logger;
		private readonly List<DiagnosticsScope> _scopes;

		private DiagnosticsScope _currentScope;

		public DiagnosticsChannel(ILogger logger)
		{
			_logger = logger;
			_currentScope = new DiagnosticsScope(0);
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
			int id = state.GetHashCode();
			_currentScope = new DiagnosticsScope(id);
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
}
