#if SNIPE_FACEBOOK

using System.Collections.Generic;
using Facebook.Unity;

namespace MiniIT.Snipe.Unity
{
	public class FacebookBinding : ConnectableAuthBinding<FacebookIdFetcher>
	{
		public FacebookBinding(ISnipeServices services)
			: base("fb", services)
		{
			UseContextIdPrefix = false;
		}

		protected override string GetAuthToken()
		{
#if MINIIT_SOCIAL_CORE_1_1
			var provider = MiniIT.Social.FacebookProvider.GetInstance(false);
			return provider?.AuthToken ?? "";
#else
			if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			{
				return AccessToken.CurrentAccessToken.TokenString;
			}
#endif
		}

		protected override void FillExtraParameters(IDictionary<string, object> data)
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
