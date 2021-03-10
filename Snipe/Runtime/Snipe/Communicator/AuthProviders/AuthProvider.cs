using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AuthProvider : IDisposable
	{
		public virtual string ProviderId { get { return "__"; } }

		public delegate void AuthSuccessCallback(int user_id, string login_token);
		public delegate void AuthFailCallback(string login_code);

		protected AuthSuccessCallback mAuthSuccessCallback;
		protected AuthFailCallback mAuthFailCallback;

		public virtual void Dispose()
		{
			mAuthSuccessCallback = null;
			mAuthFailCallback = null;
		}

		public virtual void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
		{
			// Override this method.

			//mAuthSuccessCallback = success_callback;
			//mAuthFailCallback = fail_callback;

			InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
		}

		protected void RequestLogin(string provider, string login, string token, bool reset_auth = false)
		{
			SnipeObject data = new SnipeObject()
			{
				["messageType"] = SnipeMessageTypes.AUTH_USER_LOGIN,
				["provider"] = provider,
				["login"] = login,
				["auth"] = token,
			};
			if (reset_auth)
				data["resetInternalAuth"] = reset_auth;

			SingleRequestClient.Request(SnipeConfig.Instance.AuthWebsocketURL, data, OnAuthLoginResponse);
		}

		protected virtual void OnAuthLoginResponse(SnipeObject data)
		{
			if (data?.SafeGetString("errorCode") == SnipeErrorCodes.OK)
			{
				//LoggedIn = true;

				string auth_login = data?.SafeGetString("internalUID");
				string auth_token = data?.SafeGetString("internalPassword");

				if (!string.IsNullOrEmpty(auth_login) && !string.IsNullOrEmpty(auth_token))
				{
					PlayerPrefs.SetString(SnipePrefs.AUTH_UID, auth_login);
					PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, auth_token);
				}
			}
		}

		protected virtual void InvokeAuthSuccessCallback(int user_id, string login_token)
		{
			if (mAuthSuccessCallback != null)
				mAuthSuccessCallback.Invoke(user_id, login_token);

			mAuthSuccessCallback = null;
			mAuthFailCallback = null;
		}

		protected virtual void InvokeAuthFailCallback(string error_code)
		{
			if (mAuthFailCallback != null)
				mAuthFailCallback.Invoke(error_code);

			mAuthSuccessCallback = null;
			mAuthFailCallback = null;
		}
	}
}