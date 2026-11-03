using UnityEngine;

namespace MiniIT.Snipe
{
	public readonly struct SnipeLogRecord
	{
		/// <summary>Milliseconds since the Unix epoch (UTC).</summary>
		public long TimestampMilliseconds { get; }
		public LogType Level { get; }
		public string Message { get; }
		public string StackTrace { get; }

		public SnipeLogRecord(long timestampMilliseconds, LogType level, string message, string stackTrace)
		{
			TimestampMilliseconds = timestampMilliseconds;
			Level = level;
			Message = message;
			StackTrace = stackTrace;
		}
	}
}
