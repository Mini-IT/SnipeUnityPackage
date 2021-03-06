﻿using System;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AuthProvider : IDisposable
	{
		public virtual string ProviderId { get { return "__"; } }

		public delegate void AuthResultCallback(string error_code, int user_id = 0);
		protected AuthResultCallback mAuthResultCallback;
		
		private string mLogin;
		private string mPassword;

		public virtual void Dispose()
		{
			mAuthResultCallback = null;
		}

		public virtual void RequestAuth(AuthResultCallback callback = null, bool reset_auth = false)
		{
			// Override this method.

			//mAuthSuccessCallback = success_callback;
			//mAuthFailCallback = fail_callback;

			InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
		}

		protected void RequestLogin(string provider, string login, string password, bool reset_auth = false)
		{
			if (reset_auth)
			{
				SnipeObject data = new SnipeObject()
				{
					["ckey"] = SnipeConfig.Instance.ClientKey,
					["provider"] = provider,
					["login"] = login,
					["auth"] = password,
				};
				ResetAuthAndLogin(data);
			}
			else
			{
				DoRequestLogin(login, password);
			}
		}
		
		private void ResetAuthAndLogin(SnipeObject data)
		{
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_RESET)?.RequestAuth(data,
				(string error_code, SnipeObject response_data) =>
				{
					if (error_code == SnipeErrorCodes.OK)
					{
						DoRequestLogin(response_data.SafeGetString("uid"), response_data.SafeGetString("password"));
					}
					// else if (error_code == SnipeErrorCodes.USER_ONLINE)
					// {
						// Task.Run(() => DelayedResetAuth(data));
					// }
					else
					{
						InvokeAuthFailCallback(error_code);
					}
				}
			);
		}
		
		// private async void DelayedResetAuth(SnipeObject data)
		// {
			// await Task.Delay(1000);
			// ResetAuthAndLogin(data);
		// }
		
		protected void DoRequestLogin(string login, string password)
		{
			mLogin = login;
			mPassword = password;
			
			SnipeObject data = new SnipeObject()
			{
				["login"] = login,
				["auth"] = password,
				["loginGame"] = true,
				["version"] = SnipeClient.SNIPE_VERSION,
				["appInfo"] = SnipeConfig.Instance.AppInfo,
			};
			
			SnipeCommunicator.Instance.MessageReceived -= OnMessageReceived;
			SnipeCommunicator.Instance.MessageReceived += OnMessageReceived;
			SnipeCommunicator.Instance.Client.SendRequest(SnipeMessageTypes.AUTH_USER_LOGIN, data);
		}
		
		private void OnMessageReceived(string message_type, string error_code, SnipeObject response_data, int request_id)
		{
			if (message_type == SnipeMessageTypes.AUTH_USER_LOGIN)
			{
				SnipeCommunicator.Instance.MessageReceived -= OnMessageReceived;
				OnAuthLoginResponse(error_code, response_data);
			}
		}

		protected virtual void OnAuthLoginResponse(string error_code, SnipeObject data)
		{
			DebugLogger.Log($"[AuthProvider] OnAuthLoginResponse {error_code} {data?.ToJSONString()}");

			if (error_code == SnipeErrorCodes.OK && !string.IsNullOrEmpty(mLogin) && !string.IsNullOrEmpty(mPassword))
			{
				PlayerPrefs.SetString(SnipePrefs.AUTH_UID, mLogin);
				PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, mPassword);
			}
			
			mLogin = "";
			mPassword = "";
		}

		protected virtual void InvokeAuthSuccessCallback(int user_id)
		{
			mAuthResultCallback?.Invoke(SnipeErrorCodes.OK, user_id);
			mAuthResultCallback = null;
		}

		protected virtual void InvokeAuthFailCallback(string error_code)
		{
			mAuthResultCallback?.Invoke(error_code, 0);
			mAuthResultCallback = null;
		}
	}
}