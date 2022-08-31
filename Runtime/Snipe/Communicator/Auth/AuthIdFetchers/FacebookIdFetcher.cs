#if SNIPE_FACEBOOK

using System;
using System.Threading.Tasks;
using MiniIT.Social;
using Facebook.Unity;

namespace MiniIT.Snipe
{
	public class FacebookIdFetcher : AuthIdFetcher
	{
		private Action<string> mCallback;
		
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
				{
					Value = AccessToken.CurrentAccessToken.UserId;
				}
			}

			if (wait_initialization && string.IsNullOrEmpty(Value))
			{
				Task.Run(() => WaitForInitialization(callback));
				return;
			}

			callback?.Invoke(Value);
		}

		private async Task WaitForInitialization(Action<string> callback)
		{
			while (!FB.IsLoggedIn || AccessToken.CurrentAccessToken == null)
			{
				await Task.Delay(100);
				Value = AccessToken.CurrentAccessToken.UserId;
			}
			callback?.Invoke(Value);
		}

		private void OnFacebookProviderInitializationComplete()
		{
			FacebookProvider.InstanceInitializationComplete -= OnFacebookProviderInitializationComplete;

			if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			{
				Value = AccessToken.CurrentAccessToken.UserId;
			}

			InvokeCallback();
		}

		private void InvokeCallback()
		{
			mCallback?.Invoke(Value);
			mCallback = null;
		}
	}
}

#endif