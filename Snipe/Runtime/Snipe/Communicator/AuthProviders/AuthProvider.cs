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
			if (reset_auth)
			{
				SnipeObject data = new SnipeObject()
				{
					["ckey"] = SnipeConfig.Instance.ClientKey,
					["provider"] = provider,
					["login"] = login,
					["auth"] = token,
				};
				SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_RESET)?.RequestAuth(data,
					(string error_code, SnipeObject response_data) =>
					{
						if (error_code == "ok")
						{
							DoRequestLogin(response_data.SafeGetString("uid"), response_data.SafeGetString("password"));
						}
					}
				);
			}
			else
			{
				DoRequestLogin(login, token);
			}
		}
		
		protected void DoRequestLogin(string login, string token)
		{
			SnipeObject data = new SnipeObject()
			{
				["login"] = login,
				["auth"] = token,
				["loginGame"] = true,
				["version"] = SnipeClient.SNIPE_VERSION,
				["appInfo"] = SnipeConfig.Instance.AppInfo,
			};
			
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_LOGIN)?.RequestAuth(data, OnAuthLoginResponse);
		}

		protected virtual void OnAuthLoginResponse(string error_code, SnipeObject data)
		{
			if (error_code == SnipeErrorCodes.OK)
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