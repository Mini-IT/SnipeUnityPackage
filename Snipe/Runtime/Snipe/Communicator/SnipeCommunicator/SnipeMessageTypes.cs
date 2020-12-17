using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeMessageTypes
	{
		public const string AUTH_USER_LOGIN = "auth/user.login";
		public const string AUTH_USER_BIND = "auth/user.bind";
		public const string AUTH_USER_EXISTS = "auth/user.exists";
		public const string AUTH_USER_REGISTER = "auth/user.register";
		public const string AUTH_CLAIM_RESTORE_TOKEN = "auth/user.claimRestoreToken";
		public const string AUTH_ATTR_GET = "auth/attr.get";
		
		public const string USER_LOGIN = "user.login";
		public const string ACTION_RUN = "action.run";
		
		public const string PREFIX_ROOM = "room.";
		public const string ROOM_JOIN = "room.join";
		public const string ROOM_DEAD = "room.dead";
		public const string ROOM_LOGOUT = "room.logout";
	}
}