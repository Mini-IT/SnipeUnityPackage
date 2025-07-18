using MiniIT;

namespace MiniIT.Snipe
{
	public static class SnipeErrorCodes
	{
		public const string OK = "ok";
		public const string WRONG_TOKEN = "wrongToken";
		public const string USER_NOT_FOUND = "userNotFound";
		public const string ALREADY_IN_ROOM = "alreadyInRoom";
		public const string ALREADY_LOGGED_IN = "alreadyLoggedIn";

		// Auth
		public const string NOT_INITIALIZED = "notInitialized";
		public const string NO_SUCH_USER = "noSuchUser";
		public const string NO_SUCH_AUTH = "noSuchAuth";
		public const string PARAMS_WRONG = "paramsWrong";
		public const string USER_ONLINE = "userOnline";
		public const string LOGOUT_IN_PROGRESS = "logoutInProgress";
		public const string LOGIN_DATA_WRONG = "loginDataWrong";

		// SnipeCommunicatorRequest
		public const string NOT_READY = "notReady";
		public const string INVALID_DATA = "invalidData";
		public const string SERVICE_OFFLINE = "serviceOffline";
		public const string GAME_SERVERS_OFFLINE = "gameServersOffline";
	}
}
