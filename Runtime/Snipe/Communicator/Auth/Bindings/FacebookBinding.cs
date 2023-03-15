#if SNIPE_FACEBOOK

using Facebook.Unity;

namespace MiniIT.Snipe
{
	public class FacebookBinding : AuthBinding<FacebookIdFetcher>
	{
		public FacebookBinding() : base("fb")
		{
		}

		protected override string GetAuthPassword()
		{
			if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			{
				return AccessToken.CurrentAccessToken.TokenString;
			}
			
			return "";
		}
	}
}

#endif