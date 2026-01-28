using MiniIT.Snipe;

namespace MiniIT.Snipe.Unity
{
	public static class SnipeUnityDefaults
	{
		public static ISnipeServices CreateDefaultServices()
		{
			return new SnipeServiceLocator(new UnitySnipeServicesFactory());
		}

		public static SnipeClientBuilder CreateDefaultBuilder()
		{
			return new SnipeClientBuilder().UseServices(CreateDefaultServices());
		}
	}
}
