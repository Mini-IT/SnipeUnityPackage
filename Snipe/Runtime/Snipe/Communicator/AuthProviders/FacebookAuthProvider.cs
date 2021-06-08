﻿using System;
using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.Social;
using Facebook.Unity;

public class FacebookAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "fb";
	public override string ProviderId { get { return PROVIDER_ID; } }
	
	public FacebookAuthProvider() : base()
	{
		if (!IsBindDone && !FacebookProvider.InstanceInitialized)
		{
			FacebookProvider.InstanceInitializationComplete += OnFacebookProviderInitializationComplete;
		}
	}

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;

		if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
		{
			RequestLogin(ProviderId, AccessToken.CurrentAccessToken.UserId, AccessToken.CurrentAccessToken.TokenString, reset_auth);
			return;
		}
		
		if (!FacebookProvider.InstanceInitialized)
		{
			FacebookProvider.InstanceInitializationComplete -= OnFacebookProviderInitializationComplete;
			FacebookProvider.InstanceInitializationComplete += OnFacebookProviderInitializationComplete;
		}

		InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
	}

	private void OnFacebookProviderInitializationComplete()
	{
		DebugLogger.Log("[FacebookAuthProvider] OnFacebookProviderInitializationComplete");

		FacebookProvider.InstanceInitializationComplete -= OnFacebookProviderInitializationComplete;

		if (SnipeCommunicator.Instance.LoggedIn && !AccountExists.HasValue)
		{
			CheckAuthExists(null);
		}
	}

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		DebugLogger.Log("[FacebookAuthProvider] RequestBind");

		mBindResultCallback = bind_callback;
		
		if (IsBindDone)
		{
			InvokeBindResultCallback(SnipeErrorCodes.OK);
			return;
		}

		string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
		string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

		if (!string.IsNullOrEmpty(auth_login) && !string.IsNullOrEmpty(auth_token))
		{
			if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			{
				SnipeObject data = new SnipeObject()
				{
					["ckey"] = SnipeConfig.Instance.ClientKey,
					["provider"] = ProviderId,
					["login"] = AccessToken.CurrentAccessToken.UserId,
					["auth"] = AccessToken.CurrentAccessToken.TokenString,
					["loginInt"] = auth_login,
					["authInt"] = auth_token,
				};

				DebugLogger.Log("[FacebookAuthProvider] send user.bind " + data.ToJSONString());
				SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_BIND)?.RequestAuth(data, OnBindResponse);

				return;
			}
		}

		if (!FacebookProvider.InstanceInitialized)
		{
			FacebookProvider.InstanceInitializationComplete -= OnFacebookProviderInitializationComplete;
			FacebookProvider.InstanceInitializationComplete += OnFacebookProviderInitializationComplete;
		}

		InvokeBindResultCallback(SnipeErrorCodes.NOT_INITIALIZED);
	}

	protected override void OnAuthLoginResponse(string error_code, SnipeObject data)
	{
		base.OnAuthLoginResponse(error_code, data);

		if (error_code == SnipeErrorCodes.OK)
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
		if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
			return AccessToken.CurrentAccessToken.UserId;

		return "";
	}

	public override bool CheckAuthExists(CheckAuthExistsCallback callback = null)
	{
		if (FB.IsLoggedIn && AccessToken.CurrentAccessToken != null)
		{
			CheckAuthExists(GetUserId(), callback);
			return true;
		}

		return false;
	}

	protected override void OnBindDone()
	{
		base.OnBindDone();

		FacebookProvider.Instance.LoggedOut += () =>
		{
			IsBindDone = false;
		};
	}
}
