
namespace MiniIT.Snipe
{
	public class SnipePrefs
	{
		public static string PREFIX = "com.miniit.snipe.";

		public static string LOGIN_USER_ID { get { return PREFIX + "LoginUserID"; } }

		public static string AUTH_UID { get { return PREFIX + "AuthUID"; } }
		public static string AUTH_KEY { get { return PREFIX + "AuthKey"; } }

		public static string AUTH_BIND_DONE { get { return PREFIX + "AuthBinded_"; } }
		
		public static string UDP_URL_INDEX { get { return PREFIX + "UdpUrlIndex"; } }
		public static string WEBSOCKET_URL_INDEX { get { return PREFIX + "WebSocketUrlIndex"; } }
	}
}
