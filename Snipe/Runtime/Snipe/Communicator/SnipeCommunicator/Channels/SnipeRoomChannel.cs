using System.Collections.Generic;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeRoomChannel : SnipeServiceChannel
	{
		public SnipeRoomChannel() : base()
		{
			//mJoinMessageType = SnipeMessageTypes.ROOM_JOIN;
			//mJoinOkErrorCodes = new List<string>()  { SnipeErrorCodes.ALREADY_IN_ROOM };
			//mLeaveMessageTypes = new List<string>() { SnipeMessageTypes.ROOM_LEAVE, SnipeMessageTypes.ROOM_DEAD };
			//mNoScopeMessageTypes = new List<string>() { "matchmaking.add", "matchmaking.remove" };
			SetJoinMessageType(SnipeMessageTypes.ROOM_JOIN);
			AddJoinOkErrorCodes(SnipeErrorCodes.ALREADY_IN_ROOM);
			AddLeaveMessageTypes(SnipeMessageTypes.ROOM_LEAVE, SnipeMessageTypes.ROOM_DEAD);
			AddNoScopeMessageTypes("matchmaking.add", "matchmaking.remove");
		}
	}
	
	/*
	public class SnipeRoomChannel : SnipeChannel
	{
		protected bool mJoined = false;
		
		public override bool CheckReady()
		{
			if (!SnipeCommunicator.InstanceInitialized)
				mJoined = false;
			
			return mJoined;
		}
		
		protected override void Initialize()
		{
			SnipeCommunicator.Instance.MessageReceived += OnMessageReceived;
			SnipeCommunicator.Instance.ConnectionFailed += OnConnectionFailed;
		}
		
		private void OnConnectionFailed(bool will_restore = false)
		{
			mJoined = false;
		}
		
		private void OnMessageReceived(string message_type, string error_code, SnipeObject response_data, int request_id)
		{
			if (!mJoined && message_type == SnipeMessageTypes.ROOM_JOIN)
			{
				if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_IN_ROOM)
				{
					mJoined = true;
					RaiseGotReady();
				}
				else
				{
					mJoined = false;
					DisposeRequests();
				}
			}
			else if (mJoined && message_type == SnipeMessageTypes.ROOM_DEAD)
			{
				mJoined = false;
				DisposeRequests();
			}
		}
	}
	*/
}