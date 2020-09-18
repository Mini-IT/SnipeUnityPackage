﻿using System;
using System.Text.RegularExpressions;
using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.Social;

public class DeviceIdAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "dvid";
	public override string ProviderId { get { return PROVIDER_ID; } }

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		DebugLogger.Log("[DeviceIdAuthProvider] RequestAuth");
		
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;
		
		if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
		{
			RequestLogin(ProviderId, GetUserId(), "", reset_auth);
		}
		else
		{
			InvokeAuthFailCallback(AuthProvider.ERROR_NOT_INITIALIZED);
		}
	}

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		DebugLogger.Log("[DeviceIdAuthProvider] RequestBind");

		//NeedToBind = false;
		mBindResultCallback = bind_callback;
		
		if (IsBindDone)
		{
			InvokeBindResultCallback(ERROR_OK);
			return;
		}
		
		if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
		{
			string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
			string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

			if (string.IsNullOrEmpty(auth_login) || string.IsNullOrEmpty(auth_token))
			{
				DebugLogger.Log("[DeviceIdAuthProvider] internal uid or token is invalid");

				InvokeBindResultCallback(AuthProvider.ERROR_PARAMS_WRONG);
			}
			else
			{
				ExpandoObject data = new ExpandoObject()
				{
					["messageType"] = REQUEST_USER_BIND,
					["provider"] = ProviderId,
					["login"] = GetUserId(),
					["loginInt"] = auth_login,
					["authInt"] = auth_token,
				};

				DebugLogger.Log("[DeviceIdAuthProvider] send user.bind " + data.ToJSONString());
				SingleRequestClient.Request(SnipeConfig.Instance.auth, data, OnBindResponse);
			}
		}
		else
		{
			InvokeAuthFailCallback(AuthProvider.ERROR_NOT_INITIALIZED);
		}
	}

	protected override void OnAuthLoginResponse(ExpandoObject data)
	{
		base.OnAuthLoginResponse(data);

		string error_code = data?.SafeGetString("errorCode");

		DebugLogger.Log($"[DeviceIdAuthProvider] {error_code}");

		if (error_code == ERROR_OK)
		{
			int user_id = data.SafeGetValue<int>("id");
			string login_token = data.SafeGetString("token");

			IsBindDone = true;

			InvokeAuthSuccessCallback(user_id, login_token);
		}
		else
		{
			InvokeAuthFailCallback(error_code);
		}
	}

	public override string GetUserId()
	{
		return SystemInfo.deviceUniqueIdentifier;
	}

	public override bool CheckAuthExists(CheckAuthExistsCallback callback = null)
	{
		if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
		{
			CheckAuthExists(GetUserId(), callback);
			return true;
		}

		return false;
	}
}