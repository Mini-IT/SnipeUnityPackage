using System.Collections.Generic;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeChatChannel : SnipeServiceChannel
	{
		public SnipeChatChannel() : base()
		{
			mJoinMessageType = "chat.join";
			mLeaveMessageTypes = new List<string>() { "chat.leave" };
		}
	}
}