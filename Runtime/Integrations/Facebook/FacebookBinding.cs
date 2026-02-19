#if SNIPE_FACEBOOK

using System;
using System.Collections.Generic;
using Facebook.Unity;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe;

namespace MiniIT.Snipe.Unity
{
	public class FacebookBinding : AuthBinding<FacebookIdFetcher>
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

		public void Connect<TBinding>(BindResultCallback callback = null) where TBinding : AuthBinding
		{
			if (_authSubsystem.TryGetBinding<TBinding>(out var binding))
			{
				Connect(binding, callback);
			}
		}

		public void Connect(AuthBinding anotherBinding, BindResultCallback callback = null)
		{
			if (anotherBinding is FacebookBinding)
			{
				// It is not possible to connect to the same provider

				_logger.LogTrace("Failed connecting bindings. It is not possible to connect to the same provider.");

				callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
				return;
			}

			string pass = GetAuthToken();
			if (string.IsNullOrEmpty(pass))
			{
				_logger.LogTrace("Failed connecting bindings. Auth token is empty.");

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
				_logger.LogTrace("Failed connecting bindings. Internal auth data is empty.");

				callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
				return;
			}

			string connectProviderId = anotherBinding.ProviderId;
			string connectUserId = anotherBinding.GetUserId();

			if (!string.IsNullOrEmpty(connectUserId))
			{
				RequestConnectAuths(uid, pass, connectProviderId, connectUserId, callback);
				return;
			}

			_logger.LogTrace("Another user ID is not ready. Trying to fetch...");

			anotherBinding.Fetcher.Fetch(false, anotherUserId =>
			{
				if (string.IsNullOrEmpty(anotherUserId))
				{
					_logger.LogTrace("Failed connecting bindings. The fetched user ID is empty.");

					callback?.Invoke(this, SnipeErrorCodes.PARAMS_WRONG);
					return;
				}

				// Don't use the returned raw value directly.
				// Use GetUserId() to get the formatted one instead.
				connectUserId = anotherBinding.GetUserId();
				RequestConnectAuths(uid, pass, connectProviderId, connectUserId, callback);
			});
		}

		private void RequestConnectAuths(string uid, string pass, string connectProviderId, string connectLogin, BindResultCallback callback)
		{
			var data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = uid,
				["auth"] = pass,
				["connectLogin"] = connectLogin,
				["connectProvider"] = connectProviderId,
			};

			FillExtraParameters(data);

			new UnauthorizedRequest(_communicator, Services, SnipeMessageTypes.AUTH_CONNECT, data)
				.Request((string errorCode, IDictionary<string, object> _) =>
				{
					callback?.Invoke(this, errorCode);
				});
		}
	}
}

#endif
