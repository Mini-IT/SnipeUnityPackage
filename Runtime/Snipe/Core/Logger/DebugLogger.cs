using System.Collections.Concurrent;
using UnityEngine;
#if ZSTRING
using Cysharp.Text;
#endif

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
			
			UnityEngine.Debug.LogFormat(format, args);
		}
		
		public static void LogWarning(object message)
		{
			if (!IsEnabled)
				return;
			
			UnityEngine.Debug.Log(ApplyStyle(message));
		}
		
		public static void LogError(object message)
		{
			if (!IsEnabled)
				return;
			
			UnityEngine.Debug.Log(ApplyStyle(message));
		}

		private static string ApplyStyle(object message)
		{
			var log = (FontSize != DEFAULT_FONT_SIZE) ?
				#if ZSTRING
				ZString.Concat("<size=", FontSize, ">", message, "</size>") :
				#else
				"<size=" + FontSize.ToString()+ ">" + message + "</size>" :
				#endif
				message;
			
			#if ZSTRING
			return ZString.Format("<i>{0:dd.MM.yyyy HH:mm:ss} UTC</i> {1}", System.DateTime.UtcNow, log);
			#else
			return $"<i>{System.DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss")} UTC</i> {log}";
			#endif
		}
	}
}