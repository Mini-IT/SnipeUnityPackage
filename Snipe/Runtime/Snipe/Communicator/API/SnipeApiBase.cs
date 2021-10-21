using System;
using MiniIT;

namespace MiniIT.Snipe
{
	public static class SnipeApiBase
	{
		public static SnipeRequest CreateRequest(string message_type, SnipeObject data)
		{
			return CreateRequest(null, message_type, data);
		}
		
		public static SnipeRequest CreateRequest(SnipeChannel channel, string message_type, SnipeObject data)
		{
			if (channel == null)
				return SnipeCommunicator.Instance.CreateRequest(message_type, data);
			
			return channel.CreateRequest(message_type, data);
		}
	}
}