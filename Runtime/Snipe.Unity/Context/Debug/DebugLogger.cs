using UnityEngine;

namespace MiniIT.Snipe.Internal
{
	internal static class DebugLogger
	{
		public static void Log(LogType logType, string text)
		{
			var stackType = Application.GetStackTraceLogType(logType);
			Application.SetStackTraceLogType(logType, StackTraceLogType.ScriptOnly);
			Debug.Log(text);
			Application.SetStackTraceLogType(logType, stackType);
		}

		public static void Log(string text) => Log(LogType.Log, text);
		public static void LogWarning(string text) => Log(LogType.Warning, text);
		public static void LogError(string text) => Log(LogType.Error, text);
	}
}
