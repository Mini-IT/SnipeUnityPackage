﻿using System;
using System.Text.RegularExpressions;
using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.Social;

public class AdvertisingIdAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "adid";
	public override string ProviderId { get { return PROVIDER_ID; } }

	public static string AdvertisingId { get; private set; }

	/// <summary>
	/// Shoud the provider try to use <c>SystemInfo.deviceUniqueIdentifier</c>
	/// if current platform doesn't support advertising id
	/// </summary>
	public static bool DeviceIdFallbackEnabled = false;

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;

		void advertising_id_callback(string advertising_id, bool tracking_enabled, string error)
		{
			Debug.Log($"[AdvertisingIdAuthProvider] advertising_id : {advertising_id} , error : {error}");

			AdvertisingId = advertising_id;

			if (CheckAdvertisingId(advertising_id))
			{
				RequestLogin(ProviderId, advertising_id, "", reset_auth);
			}
			else
			{
				Debug.Log("[AdvertisingIdAuthProvider] advertising_id is invalid");

				InvokeAuthFailCallback(AuthProvider.ERROR_NOT_INITIALIZED);
			}
		}

		if (!Application.RequestAdvertisingIdentifierAsync(advertising_id_callback))
		{
			if (DeviceIdFallbackEnabled && SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
			{
				advertising_id_callback(SystemInfo.deviceUniqueIdentifier, false, "");
			}
			else
			{
				Debug.Log("[AdvertisingIdAuthProvider] advertising id is not supported on this platform");

				InvokeAuthFailCallback(AuthProvider.ERROR_NOT_INITIALIZED);
			}
		}
	}

	private bool CheckAdvertisingId(string advertising_id)
	{
		if (string.IsNullOrEmpty(advertising_id))
			return false;

		// on IOS value may be "00000000-0000-0000-0000-000000000000"
		return Regex.IsMatch(advertising_id, @"[^0\W]");
	}

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		Debug.Log("[AdvertisingIdAuthProvider] RequestBind");

		//NeedToBind = false;
		mBindResultCallback = bind_callback;

		void advertising_id_callback(string advertising_id, bool tracking_enabled, string error)
		{
			Debug.Log($"[AdvertisingIdAuthProvider] advertising_id : {advertising_id} , {error}");

			AdvertisingId = advertising_id;

			string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
			string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

			if (string.IsNullOrEmpty(auth_login) || string.IsNullOrEmpty(auth_token))
			{
				Debug.Log("[AdvertisingIdAuthProvider] internal uid or token is invalid");

				InvokeBindResultCallback(AuthProvider.ERROR_PARAMS_WRONG);
			}
			else
			{
				if (CheckAdvertisingId(advertising_id))
				{
					ExpandoObject data = new ExpandoObject()
					{
						["messageType"] = REQUEST_USER_BIND,
						["provider"] = ProviderId,
						["login"] = advertising_id,
						["loginInt"] = auth_login,
						["authInt"] = auth_token,
					};

					Debug.Log("[AdvertisingIdAuthProvider] send user.bind " + data.ToJSONString());
					SingleRequestClient.Request(SnipeConfig.Instance.auth, data, OnBindResponse);
				}
				else
				{
					Debug.Log("[AdvertisingIdAuthProvider] advertising_id is invalid");

					InvokeBindResultCallback(AuthProvider.ERROR_NOT_INITIALIZED);
				}
			}
		}

		if (!Application.RequestAdvertisingIdentifierAsync(advertising_id_callback))
		{
			if (DeviceIdFallbackEnabled && SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
			{
				advertising_id_callback(SystemInfo.deviceUniqueIdentifier, false, "");
			}
			else
			{
				Debug.Log("[AdvertisingIdAuthProvider] advertising id is not supported on this platform");

				InvokeAuthFailCallback(AuthProvider.ERROR_NOT_INITIALIZED);
			}
		}
	}

	protected override void OnAuthLoginResponse(ExpandoObject data)
	{
		base.OnAuthLoginResponse(data);

		string error_code = data?.SafeGetString("errorCode");

		Debug.Log($"[AdvertisingIdAuthProvider] {error_code}");

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
		return AdvertisingId;
	}

	public override bool CheckAuthExists(CheckAuthExistsCallback callback = null)
	{
		void advertising_id_callback(string advertising_id, bool tracking_enabled, string error)
		{
			Debug.Log($"[AdvertisingIdAuthProvider] CheckAuthExists - advertising_id : {advertising_id} , error : {error}");

			AdvertisingId = advertising_id;

			if (CheckAdvertisingId(advertising_id))
			{
				CheckAuthExists(AdvertisingId, callback);
			}
			else
			{
				Debug.Log("[AdvertisingIdAuthProvider] CheckAuthExists - advertising_id is invalid");

				if (callback != null)
					callback.Invoke(this, false, false);
			}
		}

		if (!Application.RequestAdvertisingIdentifierAsync(advertising_id_callback))
		{
			if (DeviceIdFallbackEnabled && SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
			{
				advertising_id_callback(SystemInfo.deviceUniqueIdentifier, false, "");
				return true;
			}

			return false;
		}

		return true;
	}
}
