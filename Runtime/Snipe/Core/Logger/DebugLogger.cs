using System.Collections.Concurrent;
using UnityEngine;

namespace MiniIT
{
	public class DebugLogger
	{
		private const int DEFAULT_FONT_SIZE = 12;

		public static bool IsEnabled = true;
		public static int FontSize = 12;
		
		#region Log RichText

		public static void LogBold(object message)
		{
			Log("<b>" + message + "</b>");
		}

		public static void LogItalic(object message)
		{
			Log("<i>" + message + "</i>");
		}

		public static void LogColor(object message, string color)
		{
			Log("<color=" + color + ">" + message + "</color>");
		}

		#endregion

		public static void Log(object message)
		{
			if (IsEnabled)
			{
				UnityEngine.Debug.Log(ApplyStyle(message));
			}
		}
		
		public static void LogFormat(string format, params object[] args)
		{
			if (IsEnabled)
			{
				UnityEngine.Debug.LogFormat(format, args);
			}
		}
		
		public static void LogWarning(object message)
		{
			if (IsEnabled)
			{
				UnityEngine.Debug.Log(ApplyStyle(message));
			}
		}
		
		public static void LogError(object message)
		{
			if (IsEnabled)
			{
				UnityEngine.Debug.Log(ApplyStyle(message));
			}
		}

		private static string ApplyStyle(object message)
		{
			var log = (FontSize != DEFAULT_FONT_SIZE) ?
				"<size=" + FontSize.ToString()+ ">" + message + "</size>" :
				message;
			
			return $"<i>{System.DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")} UTC</i> {log}";
		}
	}
}