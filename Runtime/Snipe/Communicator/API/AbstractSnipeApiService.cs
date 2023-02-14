
namespace MiniIT.Snipe.Api
{
	public class AbstractSnipeApiService
	{
		public SnipeCommunicatorRequest CreateRequest(string message_type, SnipeObject data)
		{
			if (SnipeCommunicator.Instance.LoggedIn || SnipeCommunicator.Instance.AllowRequestsToWaitForLogin)
			{
				return SnipeCommunicator.Instance.CreateRequest(message_type, data);
			}
			
			return null;
		}
	}
}