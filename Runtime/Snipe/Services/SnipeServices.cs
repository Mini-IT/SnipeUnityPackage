
namespace MiniIT.Snipe
{
	public class SnipeServices
	{
		private static ISnipeServices s_instance;
		public static ISnipeServices Instance => s_instance ?? new UnitySnipeServices();
	}
}
