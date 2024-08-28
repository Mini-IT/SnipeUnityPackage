#if SNIPE_FACEBOOK

using System;
using System.Threading.Tasks;
using MiniIT.Threading.Tasks;
using Facebook.Unity;

namespace MiniIT.Snipe.Unity
{
	public class FacebookIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value) && FB.IsLoggedIn)
			{
				string userId = GetFacebookUserId();
				if (!string.IsNullOrEmpty(userId))
				{
					SetValue(userId);
				}
			}

			if (wait_initialization && string.IsNullOrEmpty(Value))
			{
				WaitForInitialization(callback);
				return;
			}

			callback?.Invoke(Value);
		}

		private async void WaitForInitialization(Action<string> callback)
		{
			string userId = GetFacebookUserId();

			while (string.IsNullOrEmpty(userId))
			{
				await AlterTask.Delay(100);
				userId = GetFacebookUserId();
			}

			SetValue(userId);

			if (!string.IsNullOrEmpty(Value))
			{
				callback?.Invoke(Value);
			}
		}

		private static string GetFacebookUserId()
		{
			// NOTE:
			// we need to wait for FacebookProvider.InstanceInitialized
			// because of `auth.bind` request that needs FB auth token.
			// AccessToken.CurrentAccessToken?.UserId may become valid before getting the token
			//
			// TODO: remove this condition and change AuthBinding.Bind() method

			if (!MiniIT.Social.FacebookProvider.InstanceInitialized)
			{
				return "";
			}

#if MINIIT_SOCIAL_CORE_1_1
			string uid = MiniIT.Social.FacebookProvider.Instance.GetPlayerUserID();
			if (!CheckValueValid(uid))
			{
				uid = AccessToken.CurrentAccessToken?.UserId;
			}
#else
			string uid = AccessToken.CurrentAccessToken?.UserId;
#endif

			if (CheckValueValid(uid))
			{
				return uid;
			}

			return "";
		}

		private void SetValue(string value)
		{
			if (CheckValueValid(value))
			{
				Value = value;
			}
			else
			{
				Value = "";
			}
		}

		private static bool CheckValueValid(string value)
		{
			return value != null && value.Length > 2;
		}
	}
}

#endif
