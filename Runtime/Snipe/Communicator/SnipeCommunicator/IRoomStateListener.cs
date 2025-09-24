namespace MiniIT.Snipe
{
	internal interface IRoomStateListener
	{
		void OnMatchmakingStarted();
		void OnRoomJoined();
		void OnRoomLeft();
	}
}
