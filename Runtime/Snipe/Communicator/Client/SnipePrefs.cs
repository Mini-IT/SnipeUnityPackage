
namespace MiniIT.Snipe
{
	public class SnipePrefs
	{
		private static string PREFIX = "com.miniit.snipe.";

		public static string GetLoginUserID(string contextId) => PREFIX + contextId + "LoginUserID";

		public static string GetAuthUID(string contextId) => PREFIX + contextId + "AuthUID";
		public static string GetAuthKey(string contextId) => PREFIX + contextId + "AuthKey";

		public static string GetAuthBindDone(string contextId) => PREFIX + contextId + "AuthBinded_";
		
		public static string GetUdpUrlIndex(string contextId) => PREFIX + contextId + "UdpUrlIndex";
		public static string GetWebSocketUrlIndex(string contextId) => PREFIX + contextId + "WebSocketUrlIndex";
	}
}
