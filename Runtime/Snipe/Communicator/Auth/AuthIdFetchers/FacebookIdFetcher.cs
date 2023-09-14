#if SNIPE_FACEBOOK

using System;
using System.Threading.Tasks;
using Facebook.Unity;

namespace MiniIT.Snipe
{
	public class FacebookIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
				{
					SetValue(AccessToken.CurrentAccessToken.UserId);
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
			while (string.IsNullOrEmpty(AccessToken.CurrentAccessToken?.UserId))
			{
				await Task.Delay(100);
			}

			SetValue(AccessToken.CurrentAccessToken.UserId);

			if (!string.IsNullOrEmpty(Value))
			{
				callback?.Invoke(Value);
			}
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
