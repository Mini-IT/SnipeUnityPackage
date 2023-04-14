
namespace MiniIT.Snipe
{
	public class SnipePrefs
	{
		private static string PREFIX = "com.miniit.snipe.";

		public static string LoginUserID(string contextId) => PREFIX + contextId + "LoginUserID";

		public static string AuthUID(string contextId) => PREFIX + contextId + "AuthUID";
		public static string AuthKey(string contextId) => PREFIX + contextId + "AuthKey";

		public static string AuthBindDone(string contextId) => PREFIX + contextId + "AuthBinded_";
		
		public static string UdpUrlIndex(string contextId) => PREFIX + contextId + "UdpUrlIndex";
		public static string WebSocketUrlIndex(string contextId) => PREFIX + contextId + "WebSocketUrlIndex";
	}
}
