
namespace MiniIT.Snipe
{
	public class AmazonBinding : AuthBinding<AmazonIdFetcher>
	{
		public AmazonBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem)
			: base("amzn", communicator, authSubsystem)
		{
		}

		public void SetUserId(string uid)
		{
			if (_fetcher is AmazonIdFetcher fetcher)
			{
				fetcher.SetValue(uid);
			}
		}
	}
}
