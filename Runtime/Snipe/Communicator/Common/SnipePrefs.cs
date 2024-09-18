
namespace MiniIT.Snipe
{
	public static class SnipePrefs
	{
		private const string PREFIX = "com.miniit.snipe.";

		public static string GetLoginUserID(int contextId) => PREFIX + contextId + "LoginUserID";

		public static string GetAuthUID(int contextId) => PREFIX + contextId + "AuthUID";
		public static string GetAuthKey(int contextId) => PREFIX + contextId + "AuthKey";

		public static string GetAuthBindDone(int contextId) => PREFIX + contextId + "AuthBinded_";
		
		public static string GetUdpUrlIndex(int contextId) => PREFIX + contextId + "UdpUrlIndex";
		public static string GetWebSocketUrlIndex(int contextId) => PREFIX + contextId + "WebSocketUrlIndex";
	}
}
