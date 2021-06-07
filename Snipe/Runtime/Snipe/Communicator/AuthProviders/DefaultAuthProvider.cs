using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class DefaultAuthProvider : AuthProvider
	{
		public const string PROVIDER_ID = "__";
		public override string ProviderId { get { return PROVIDER_ID; } }


		public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
		{
			mAuthSuccessCallback = success_callback;
			mAuthFailCallback = fail_callback;

			string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
			string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

			if (!string.IsNullOrEmpty(auth_login) && !string.IsNullOrEmpty(auth_token))
			{
				RequestLogin(ProviderId, auth_login, auth_token, reset_auth);
			}
			else
			{
				InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
			}
		}

		protected override void OnAuthLoginResponse(string error_code, SnipeObject data)
		{
			base.OnAuthLoginResponse(error_code, data);

			if (mAuthSuccessCallback == null)
				return;

			if (error_code == SnipeErrorCodes.OK)
			{
				int user_id = data.SafeGetValue<int>("id");
				string login_token = data.SafeGetString("token");

				InvokeAuthSuccessCallback(user_id, login_token);
			}
			else
			{
				if (error_code == SnipeErrorCodes.NO_SUCH_USER)
				{
					PlayerPrefs.DeleteKey(SnipePrefs.AUTH_UID);
					PlayerPrefs.DeleteKey(SnipePrefs.AUTH_KEY);
				}

				InvokeAuthFailCallback(error_code);
			}
		}
	}
}