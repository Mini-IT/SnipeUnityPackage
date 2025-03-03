
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiModule
	{
		protected AbstractSnipeApiService _snipeApiService;

		public SnipeApiModule(AbstractSnipeApiService snipeApiService)
		{
			_snipeApiService = snipeApiService;
			_snipeApiService.AddModule(this);
		}

		protected AbstractCommunicatorRequest CreateRequest(string messageType, IDictionary<string, object> data = null)
			=> _snipeApiService.CreateRequest(messageType, data);

		protected void SubscribeOnMessageReceived(SnipeCommunicator.MessageReceivedHandler handler)
			=> _snipeApiService.SubscribeOnMessageReceived(handler);
	}
}
