using System;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeApiBase
	{
		public SnipeCommunicator Communicator { get; private set; }

		public SnipeApiBase(SnipeCommunicator communicator)
		{
			this.Communicator = communicator;
		}

		
		public SnipeCommunicatorRequest CreateRequest(string message_type, ExpandoObject data)
		{
			if (Communicator == null)
				return null;
			
			if (Communicator.LoggedIn || Communicator.AllowRequestsToWaitForLogin || Communicator.KeepOfflineRequests)
			{
				return Communicator.CreateRequest(message_type, data);
			}
			
			return null;
		}
	}
}