using System;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public static class SnipeServices
	{
		public static bool IsInitialized => s_locator != null;
		public static ISnipeServiceLocator Instance => s_locator;

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
