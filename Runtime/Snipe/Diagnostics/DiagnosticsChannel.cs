using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public class DiagnosticsChannel : IDiagnosticsChannel
	{
		private readonly ILogger _logger;
		private readonly Dictionary<int, DiagnosticsScope> _scopes;

		private DiagnosticsScope _currentScope;

		public DiagnosticsChannel(ILogger logger)
		{
			_logger = logger;
			_currentScope = new DiagnosticsScope(0);
			_scopes = new Dictionary<int, DiagnosticsScope>(1)
			{
				[_currentScope.Id] = _currentScope
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

			if (_scopes.TryGetValue(id, out var scope))
			{
				_currentScope = scope;
			}
			else
			{
				scope = new DiagnosticsScope(id);
				_scopes[id] = scope;
				_currentScope = scope;
			}

			return _currentScope; //_logger.BeginScope(state);
		}

		#endregion

		public void Dispose()
		{
			_currentScope = null;
			foreach (var scope in _scopes)
			{
				scope.Value.Dispose();
			}
			_scopes.Clear();
		}
	}
}
