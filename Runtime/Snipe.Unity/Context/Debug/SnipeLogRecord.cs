using UnityEngine;

namespace MiniIT.Snipe
{
	public readonly struct SnipeLogRecord
	{
		public long Time { get; }
		public LogType Level { get; }
		public string Message { get; }
		public string StackTrace { get; }

		public SnipeLogRecord(long time, LogType level, string message, string stackTrace)
		{
			Time = time;
			Level = level;
			Message = message;
			StackTrace = stackTrace;
		}
	}
}
