using System;
using MiniIT.Snipe;

namespace MiniIT.Snipe.Unity
{
	public static class SnipeUnityBuilder
	{
		public static SnipeClientBuilder CreateDefault()
		{
			return SnipeUnityDefaults.CreateDefaultBuilder();
		}

		public static SnipeClientBuilder Create(ISnipeServiceLocatorFactory factory)
		{
			if (factory == null)
			{
				throw new ArgumentNullException(nameof(factory));
			}

			var services = new SnipeServiceLocator(factory);
			return new SnipeClientBuilder().UseServices(services);
		}
	}
}
