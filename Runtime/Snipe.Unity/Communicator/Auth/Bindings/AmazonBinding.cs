
namespace MiniIT.Snipe.Unity
{
	public class AmazonBinding : AuthBinding<AmazonIdFetcher>
	{
		public AmazonBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base("amzn", communicator, authSubsystem, config)
		{
		}

		public void SetUserId(string uid)
		{
			if (Fetcher is AmazonIdFetcher fetcher)
			{
				fetcher.SetValue(uid);
			}
		}
	}
}
