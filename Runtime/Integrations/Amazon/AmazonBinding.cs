
namespace MiniIT.Snipe.Unity
{
	public class AmazonBinding : AuthBinding<AmazonIdFetcher>
	{
		public AmazonBinding()
			: base("amzn")
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
