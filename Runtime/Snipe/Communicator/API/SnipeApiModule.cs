
namespace MiniIT.Snipe.Api
{
	public class SnipeApiModule
	{
		protected AbstractSnipeApiService _snipeApiService;
		
		public SnipeApiModule(AbstractSnipeApiService snipeApiService)
		{
			_snipeApiService = snipeApiService;
		}
		
		public AbstractCommunicatorRequest CreateRequest(string messageType, SnipeObject data = null)
			=> _snipeApiService.CreateRequest(messageType, data);
	}
}
