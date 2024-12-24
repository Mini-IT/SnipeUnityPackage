using System;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;

namespace MiniIT.Snipe
{
	public static class SnipeServices
	{
		#region static ISnipeServiceLocator

		public static ISharedPrefs SharedPrefs => s_locator.SharedPrefs;
		public static ILogService LogService => s_locator.LogService;
		public static ISnipeAnalyticsService Analytics => s_locator.Analytics;
		public static IMainThreadRunner MainThreadRunner => s_locator.MainThreadRunner;
		public static IApplicationInfo ApplicationInfo => s_locator.ApplicationInfo;
		public static IStopwatchFactory FuzzyStopwatchFactory => s_locator.FuzzyStopwatchFactory;

		#endregion

		public static bool IsInitialized => s_locator != null;

		private static ISnipeServiceLocator s_locator;

		public static void Initialize(ISnipeServiceLocatorFactory factory)
		{
			if (s_locator != null)
			{
				try
				{
					var logger = s_locator.LogService.GetLogger(nameof(SnipeServices));
					logger.LogError($"Locator is already initialized. Call `{nameof(Dispose)}` first if you need to reinitialize it with another factory.");
				}
				catch (Exception)
				{
					// ignore
				}
				return;
			}

			s_locator = new SnipeServiceLocator(factory);
		}

		public static void Dispose()
		{
			if (s_locator != null)
			{
				s_locator.Dispose();
				s_locator = null;
			}
		}
	}
}
