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

		public SnipeCommunicatorRequest CreateRequest(ExpandoObject data)
		{
			if (Communicator == null)
				return null;
			
			if (Communicator.LoggedIn || Communicator.AllowRequestsToWaitForLogin || Communicator.KeepOfflineRequests)
			{
				var request = Communicator.CreateRequest();
				request.Data = data;
				return request;
			}
			
			return null;
		}

		public SnipeServiceRequest CreateServiceRequest(ExpandoObject data)
		{
			if (Communicator == null || Communicator.ServiceCommunicator == null || !Communicator.ServiceCommunicator.Ready)
				return null;

			var request = Communicator.ServiceCommunicator.CreateRequest();
			request.Data = data;
			return request;
		}
	}
}