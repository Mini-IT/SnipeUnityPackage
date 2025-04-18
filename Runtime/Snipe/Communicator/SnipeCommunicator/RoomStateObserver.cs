namespace MiniIT.Snipe
{
	internal interface IRoomStateListener
	{
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

		public void OnMessageReceived(string messageType, string errorCode)
		{
			if (messageType == SnipeMessageTypes.ROOM_JOIN)
			{
				if (errorCode == SnipeErrorCodes.OK || errorCode == SnipeErrorCodes.ALREADY_IN_ROOM)
				{
					if (_roomState != RoomState.Joined)
					{
						_roomState = RoomState.Joined;
						_roomStateListener.OnRoomJoined();
					}
				}
				else
				{
					SetNotInRoom();
				}
			}
			else if (messageType == SnipeMessageTypes.ROOM_DEAD)
			{
				SetNotInRoom();
			}
		}

		private void SetNotInRoom()
		{
			bool consideredJoined = _roomState == RoomState.Joined;

			_roomState = RoomState.NotInRoom;

			if (consideredJoined)
			{
				_roomStateListener.OnRoomLeft();
			}
		}
	}
}
