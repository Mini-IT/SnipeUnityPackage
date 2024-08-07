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

		protected override string GetAuthToken()
		{
#if MINIIT_SOCIAL_CORE_1_1
			if (MiniIT.Social.FacebookProvider.InstanceInitialized)
			{
				return MiniIT.Social.FacebookProvider.Instance.AuthToken ?? "";
			}
#else
			if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			{
				return AccessToken.CurrentAccessToken.TokenString;
			}
#endif
			
			return "";
		}

		protected override void FillExtraParameters(SnipeObject data)
		{
#if MINIIT_SOCIAL_CORE_1_1
			if (MiniIT.Social.FacebookProvider.InstanceInitialized && MiniIT.Social.FacebookProvider.Instance.IsTrackingLimited)
			{
				data["version"] = 2;
			}
#endif
		}
	}
}

#endif
