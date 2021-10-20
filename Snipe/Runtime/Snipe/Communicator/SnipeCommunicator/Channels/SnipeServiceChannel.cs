using System.Collections.Generic;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeServiceChannel : SnipeChannel
	{
		protected bool mJoined = false;
		
		protected string mJoinMessageType;
		protected List<string> mLeaveMessageTypes;
		protected List<string> mJoinOkErrorCodes;
		
		public override bool CheckReady()
		{
			if (!SnipeCommunicator.InstanceInitialized)
				mJoined = false;
			
			return mJoined;
		}
		
		public void SetJoinMessageType(string message_type)
		{
			mJoinMessageType = message_type;
		}
		
		public void AddLeaveMessageTypes(params string[] message_types)
		{
			AddStringItems(ref mLeaveMessageTypes, message_types);
		}
		
		public void AddJoinOkErrorCodes(params string[] error_codes)
		{
			AddStringItems(ref mJoinOkErrorCodes, error_codes);
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
			if (!mJoined && message_type == mJoinMessageType)
			{
				mJoined = (error_code == SnipeErrorCodes.OK) || (mJoinOkErrorCodes != null && mJoinOkErrorCodes.Contains(error_code));
				
				if (mJoined)
				{
					RaiseGotReady();
				}
				else
				{
					DisposeRequests();
				}
			}
			else if (mJoined && mLeaveMessageTypes != null && mLeaveMessageTypes.Contains(message_type))
			{
				mJoined = false;
				DisposeRequests();
			}
		}
		
		public override bool CheckNoScopeMessageType(string message_type)
		{
			return message_type == mJoinMessageType || base.CheckNoScopeMessageType(message_type);
		}
	}
}