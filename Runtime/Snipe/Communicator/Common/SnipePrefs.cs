using System.Runtime.CompilerServices;

namespace MiniIT.Snipe
{
	public static class SnipePrefs
	{
		private const string PREFIX = "com.miniit.snipe.";

		public static string GetLoginUserID(int contextID)
			=> GetPrefixedString(contextID, "LoginUserID");

		public static string GetAuthUID(int contextID)
			=> GetPrefixedString(contextID, "AuthUID");
		public static string GetAuthKey(int contextID)
			=> GetPrefixedString(contextID, "AuthKey");

		public static string GetAuthBindDone(int contextID)
			=> GetPrefixedString(contextID, "AuthBinded_");

		public static string GetUdpUrlIndex(int contextID)
			=> GetPrefixedString(contextID, "UdpUrlIndex");
		public static string GetWebSocketUrlIndex(int contextID)
			=> GetPrefixedString(contextID, "WebSocketUrlIndex");
		public static string GetHttpUrlIndex(int contextID)
			=> GetPrefixedString(contextID, "HttpUrlIndex");

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static string GetPrefixedString(int contextID, string value)
			=> PREFIX + (contextID != 0 ? contextID.ToString() : "") + value;
	}
}
