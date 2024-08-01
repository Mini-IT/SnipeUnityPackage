#if SNIPE_FACEBOOK

using System;
using System.Threading.Tasks;
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
				await Task.Delay(100);
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
#if MINIIT_SOCIAL_CORE_1_1
			if (MiniIT.Social.FacebookProvider.InstanceInitialized)
			{
				return MiniIT.Social.FacebookProvider.Instance.GetPlayerUserID();
			}
#endif
			return AccessToken.CurrentAccessToken?.UserId;
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
