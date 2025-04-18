using System;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Diagnostics
{
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
}
