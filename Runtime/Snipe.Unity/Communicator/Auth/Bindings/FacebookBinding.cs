#if SNIPE_FACEBOOK

using Facebook.Unity;

namespace MiniIT.Snipe.Unity
{
	public class FacebookBinding : AuthBinding<FacebookIdFetcher>
	{
		public FacebookBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base("fb", communicator, authSubsystem, config)
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
