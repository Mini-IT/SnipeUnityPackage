using System;
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
	
	private SnipeObject mBindRequestData = null;

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		DebugLogger.Log("[AdvertisingIdAuthProvider] RequestAuth");
		
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;

		void advertising_id_callback(string advertising_id, bool tracking_enabled, string error)
		{
			var error_string = string.IsNullOrEmpty(error) ? "" : ", error: " + error;
			DebugLogger.Log($"[AdvertisingIdAuthProvider] advertising_id : {advertising_id} {error_string}");

			AdvertisingId = advertising_id;

			if (CheckAdvertisingId(advertising_id))
			{
				RequestLogin(ProviderId, advertising_id, "", reset_auth);
			}
			else
			{
				DebugLogger.Log("[AdvertisingIdAuthProvider] advertising id is invalid");
				
				InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
			}
		}

		if (!Application.RequestAdvertisingIdentifierAsync(advertising_id_callback))
		{
			DebugLogger.Log("[AdvertisingIdAuthProvider] advertising id is not supported on this platform");
			
			InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
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
		DebugLogger.Log("[AdvertisingIdAuthProvider] RequestBind");
		
		if (mBindResultCallback != null && mBindResultCallback != bind_callback)
		{
			DebugLogger.LogWarning("[AdvertisingIdAuthProvider] Bind callback is not null. Previous callback will not be called.");
		}

		mBindResultCallback = bind_callback;
		
		if (IsBindDone)
		{
			InvokeBindResultCallback(SnipeErrorCodes.OK);
			return;
		}

		void advertising_id_callback(string advertising_id, bool tracking_enabled, string error)
		{
			DebugLogger.Log($"[AdvertisingIdAuthProvider] advertising_id : {advertising_id} , {error}");

			if (AdvertisingId == advertising_id && IsBindDone)
			{
				InvokeBindResultCallback(SnipeErrorCodes.OK);
				return;
			}
			
			AdvertisingId = advertising_id;

			string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
			string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

			if (string.IsNullOrEmpty(auth_login) || string.IsNullOrEmpty(auth_token))
			{
				DebugLogger.Log("[AdvertisingIdAuthProvider] internal uid or token is invalid");

				InvokeBindResultCallback(SnipeErrorCodes.PARAMS_WRONG);
			}
			else
			{
				if (CheckAdvertisingId(advertising_id))
				{
					SnipeObject data = new SnipeObject()
					{
						["messageType"] = SnipeMessageTypes.AUTH_USER_BIND,
						["provider"] = ProviderId,
						["login"] = advertising_id,
						["loginInt"] = auth_login,
						["authInt"] = auth_token,
					};
					
					if (mBindRequestData != null && SnipeObject.ContentEquals(mBindRequestData, data))
					{
						DebugLogger.LogWarning("[AdvertisingIdAuthProvider] Bind is already requested. This request will not be performed.");
					}
					else
					{
						mBindRequestData = data;

						DebugLogger.Log("[AdvertisingIdAuthProvider] send user.bind " + data.ToJSONString());
						SingleRequestClient.Request(SnipeConfig.Instance.AuthWebsocketURL, data, OnBindResponse);
					}
				}
				else
				{
					DebugLogger.Log("[AdvertisingIdAuthProvider] advertising_id is invalid");

					InvokeBindResultCallback(SnipeErrorCodes.NOT_INITIALIZED);
				}
			}
		}
		
		if (CheckAdvertisingId(AdvertisingId))
		{
			DebugLogger.Log("[AdvertisingIdAuthProvider] advertising id is already known");
			advertising_id_callback(AdvertisingId, false, "");
		}
		else if (!Application.RequestAdvertisingIdentifierAsync(advertising_id_callback))
		{
			DebugLogger.Log("[AdvertisingIdAuthProvider] advertising id is not supported on this platform");

			InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
		}
	}

	protected override void OnAuthLoginResponse(SnipeObject data)
	{
		base.OnAuthLoginResponse(data);

		string error_code = data?.SafeGetString("errorCode");

		DebugLogger.Log($"[AdvertisingIdAuthProvider] {error_code}");

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
	
	protected override void OnBindResponse(SnipeObject data)
	{
		mBindRequestData = null;
		
		base.OnBindResponse(data);
	}

	public override string GetUserId()
	{
		return AdvertisingId;
	}

	public override bool CheckAuthExists(CheckAuthExistsCallback callback = null)
	{
		void advertising_id_callback(string advertising_id, bool tracking_enabled, string error)
		{
			DebugLogger.Log($"[AdvertisingIdAuthProvider] CheckAuthExists - advertising_id : {advertising_id} , error : {error}");

			AdvertisingId = advertising_id;

			if (CheckAdvertisingId(advertising_id))
			{
				CheckAuthExists(AdvertisingId, callback);
			}
			else
			{
				DebugLogger.Log("[AdvertisingIdAuthProvider] CheckAuthExists - advertising_id is invalid");

				if (callback != null)
					callback.Invoke(this, false, false);
			}
		}

		return Application.RequestAdvertisingIdentifierAsync(advertising_id_callback);
	}
}
