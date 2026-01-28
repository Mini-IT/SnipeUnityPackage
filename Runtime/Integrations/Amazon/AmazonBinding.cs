
using MiniIT.Snipe;

namespace MiniIT.Snipe.Unity
{
	public class AmazonBinding : AuthBinding<AmazonIdFetcher>
	{
		public AmazonBinding(ISnipeServices services)
			: base("amzn", services)
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
