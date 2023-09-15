using System;
using Microsoft.Extensions.Logging;

namespace MiniIT.Debug.Logging.Null
{
	public class NullLoggerFactory : ILoggerFactory
	{
		public void AddProvider(ILoggerProvider provider)
		{
		}

		public ILogger CreateLogger(string categoryName)
		{
			return new NullLogger(categoryName);
		}

		public void Dispose()
		{
		}
	}
	
	public class NullLogger : ILogger
	{
		public NullLogger(string categoryName)
		{
		}

		public IDisposable BeginScope<TState>(TState state) where TState : notnull
		{
			// currently scope is not supported...
			return NullDisposable.Instance;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			
		}
	}

	internal class NullDisposable : IDisposable
	{
		public static IDisposable Instance = new NullDisposable();

		NullDisposable()
		{
		}

		public void Dispose()
		{
		}
	}
}
