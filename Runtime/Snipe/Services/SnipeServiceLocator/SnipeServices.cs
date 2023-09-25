namespace MiniIT.Snipe
{
	public static class SnipeServices
	{
		public static ISnipeServiceLocator Instance => s_instance;
		public static bool IsInitialized => s_instance != null;

		private static ISnipeServiceLocator s_instance;

		public static void Initialize(ISnipeServiceLocatorFactory factory)
		{
			s_instance = new SnipeServiceLocator(factory);
		}
	}
}
