
namespace MiniIT.Snipe
{
	public class SnipeMessageTypes
	{
		public const string USER_LOGIN = "user.login";
		public const string AUTH_BIND = "auth.bind";
		public const string AUTH_EXISTS = "auth.exists";
		public const string AUTH_REGISTER = "auth.register";
		public const string AUTH_RESTORE = "auth.restore";
		public const string AUTH_RESET = "auth.reset";
		public const string AUTH_ATTR_GET = "auth.getAttr";
		public const string AUTH_ATTR_GET_MULTI = "auth.getAttrMulti";

		public const string ATTR_GET = "attr.get";
		public const string ATTR_GET_MULTI = "attr.getMulti";
		
		public const string AUTH_REGISTER_AND_LOGIN = "auth.registerAndLogin";
		
		public const string ACTION_RUN = "action.run";
		
		public const string PREFIX_ROOM = "room.";
		public const string ROOM_JOIN = "room.join";
		public const string ROOM_DEAD = "room.dead";
		public const string ROOM_LEAVE = "room.leave";
		
		public const string LOGIC_GET = "logic.get";
		public const string LOGIC_INC_VAR = "logic.incVar";
	}
}
