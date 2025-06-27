#if SNIPE_FACEBOOK

using System;
using System.Collections.Generic;
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
			var provider = MiniIT.Social.FacebookProvider.GetInstance(false);
			return provider?.AuthToken ?? "";
#else
			if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			{
				return AccessToken.CurrentAccessToken.TokenString;
			}
#endif

			return "";
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

		public void Connect<TBinding>(BindResultCallback callback = null) where TBinding : AuthBinding
		{
			var binding = _authSubsystem.GetBinding<TBinding>();
			Connect(binding, callback);
		}

		public void Connect(AuthBinding binding, BindResultCallback callback = null)
		{
			if (binding is FacebookBinding)
			{
				// It is not possible to connect to the same provider

				callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
				return;
			}

			string pass = GetAuthToken();
			if (string.IsNullOrEmpty(pass))
			{
				callback?.Invoke(this, SnipeErrorCodes.WRONG_TOKEN);
				return;
			}

			string authLogin = GetInternalAuthLogin();
			string authToken = GetInternalAuthToken();
			string uid = GetUserId();

			if (string.IsNullOrEmpty(authLogin) ||
				string.IsNullOrEmpty(authToken) ||
				string.IsNullOrEmpty(uid))
			{
				return;
			}

			var data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = uid,
				["auth"] = pass,
				["connectLogin"] = _config.ContextId + binding.GetUserId(),
				["connectProvider"] = binding.ProviderId,
			};

			FillExtraParameters(data);

			//_logger.LogTrace($"({ProviderId}) send user.bind " + data.ToJSONString());
			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_CONNECT, data)
				.Request((string errorCode, IDictionary<string, object> _) =>
				{
					callback?.Invoke(this, errorCode);
				});
		}
	}
}

#endif
