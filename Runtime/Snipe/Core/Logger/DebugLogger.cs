using System.Collections.Concurrent;
using UnityEngine;
#if ZSTRING
using Cysharp.Text;
#endif

namespace MiniIT
{
	public class DebugLogger
	{
		public static bool IsEnabled = true;
		
		#region Log RichText

		public static void LogBold(object message)
		{
			if (!IsEnabled)
				return;
			
			#if ZSTRING
			Log(ZString.Concat("<b>", message, "</b>"));
			#else
			Log("<b>" + message + "</b>");
			#endif
		}

		public static void LogItalic(object message)
		{
			if (!IsEnabled)
				return;
			
			#if ZSTRING
			Log(ZString.Concat("<i>", message, "</i>"));
			#else
			Log("<i>" + message + "</i>");
			#endif
		}

		public static void LogColor(object message, string color)
		{
			if (!IsEnabled)
				return;
			
			#if ZSTRING
			Log(ZString.Concat("<color=", color, ">", message, "</color>"));
			#else
			Log("<color=" + color + ">" + message + "</color>");
			#endif
		}

		#endregion

		public static void Log(object message)
		{
			if (!IsEnabled)
				return;
			
			UnityEngine.Debug.Log(ApplyStyle(message));
		}
		
		public static void LogFormat(string format, params object[] args)
		{
			if (!IsEnabled)
				return;

#if ZSTRING
			UnityEngine.Debug.Log(ApplyStyle(ZString.Format(format, args)));
#else
			UnityEngine.Debug.LogFormat(format, args);
#endif
		}
		
		public static void LogWarning(object message)
		{
			if (!IsEnabled)
				return;
			
			UnityEngine.Debug.LogWarning(ApplyStyle(message));
		}
		
		public static void LogError(object message)
		{
			if (!IsEnabled)
				return;
			
			UnityEngine.Debug.LogError(ApplyStyle(message));
		}

		private static string ApplyStyle(object message)
		{
			string now = System.DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss");
#if ZSTRING
			return ZString.Concat("<i>", now, " UTC</i> ", message);
#else
			return $"<i>{now} UTC</i> {message}";
#endif
		}
	}
}
