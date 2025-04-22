namespace MiniIT.Snipe
{
	internal interface IRoomStateListener
	{
		void OnMatchmakingStarted();
		void OnRoomJoined();
		void OnRoomLeft();
	}

	internal class RoomStateObserver
	{
		public RoomState State => _roomState;

		private RoomState _roomState = RoomState.Unknown;

		private readonly IRoomStateListener _roomStateListener;

		public RoomStateObserver(IRoomStateListener roomStateListener)
		{
			_roomStateListener = roomStateListener;
		}

		public void Reset()
		{
			_roomState = RoomState.Unknown;
		}

		public void OnRequestSent(string messageType)
		{
			switch (messageType)
			{
				case SnipeMessageTypes.MATCHMAKING_ADD:
					SetMatchmaking();
					break;
			}
		}

		public void OnMessageReceived(string messageType, string errorCode)
		{
			switch (messageType)
			{
				case SnipeMessageTypes.ROOM_JOIN:
				{
					switch (errorCode)
					{
						case SnipeErrorCodes.OK:
						case SnipeErrorCodes.ALREADY_IN_ROOM:
							SetRoomJoined();
							break;
						default:
							SetNotInRoom();
							break;
					}

					break;
				}

				case SnipeMessageTypes.ROOM_DEAD:
				case SnipeMessageTypes.ROOM_LEAVE:
				case SnipeMessageTypes.MATCHMAKING_REMOVE:
					SetNotInRoom();
					break;

				case SnipeMessageTypes.MATCHMAKING_START:
					SetMatchmaking();
					break;
			}
		}

		private void SetRoomJoined()
		{
			if (_roomState == RoomState.Joined)
			{
				return;
			}

			_roomState = RoomState.Joined;
			_roomStateListener.OnRoomJoined();
		}

		private void SetNotInRoom()
		{
			bool wasJoined = (_roomState == RoomState.Joined);

			_roomState = RoomState.NotInRoom;

			if (wasJoined)
			{
				_roomStateListener.OnRoomLeft();
			}
		}

		private void SetMatchmaking()
		{
			if (_roomState == RoomState.Matchmaking)
			{
				return;
			}

			_roomState = RoomState.Matchmaking;
			_roomStateListener.OnMatchmakingStarted();
		}
	}
}
