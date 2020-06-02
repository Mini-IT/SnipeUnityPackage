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

		public SnipeRequest CreateRequest(ExpandoObject data)
		{
			if (Communicator == null || !Communicator.LoggedIn)
				return null;

			SnipeRequest request = Communicator.CreateRequest();
			request.Data = data;
			return request;
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