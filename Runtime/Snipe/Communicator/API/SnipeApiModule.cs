using System;
using MiniIT;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiModule
	{
		protected AbstractSnipeApiService _snipeApiService;
		
		public SnipeApiModule(AbstractSnipeApiService snipeApiService)
		{
			_snipeApiService = snipeApiService;
		}
		
		public SnipeCommunicatorRequest CreateRequest(string messageType, SnipeObject data) => _snipeApiService.CreateRequest(messageType, data);
	}
}