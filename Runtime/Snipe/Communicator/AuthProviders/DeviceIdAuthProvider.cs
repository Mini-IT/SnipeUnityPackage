﻿using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using System.Security.Cryptography;
using System.Text;

public class DeviceIdAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "dvid";
	public override string ProviderId { get { return PROVIDER_ID; } }

	private string _deviceId = null;

	public override void RequestAuth(AuthResultCallback callback = null, bool reset_auth = false)
	{
		DebugLogger.Log("[DeviceIdAuthProvider] RequestAuth");

		mAuthResultCallback = callback;

		if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
		{
			RequestLogin(ProviderId, GetUserId(), "", reset_auth);
		}
		else
		{
			InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
		}
	}

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		DebugLogger.Log("[DeviceIdAuthProvider] RequestBind");

		mBindResultCallback = bind_callback;

		if (IsBindDone)
		{
			InvokeBindResultCallback(SnipeErrorCodes.OK);
			return;
		}

		if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
		{
			string auth_login = SharedPrefs.GetString(SnipePrefs.AUTH_UID);
			string auth_token = SharedPrefs.GetString(SnipePrefs.AUTH_KEY);

			if (string.IsNullOrEmpty(auth_login) || string.IsNullOrEmpty(auth_token))
			{
				DebugLogger.Log("[DeviceIdAuthProvider] internal uid or token is invalid");

				InvokeBindResultCallback(SnipeErrorCodes.PARAMS_WRONG);
			}
			else
			{
				SnipeObject data = new SnipeObject()
				{
					["ckey"] = SnipeConfig.ClientKey,
					["provider"] = ProviderId,
					["login"] = GetUserId(),
					["loginInt"] = auth_login,
					["authInt"] = auth_token,
				};

				DebugLogger.Log("[DeviceIdAuthProvider] send user.bind " + data.ToJSONString());
				SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_BIND)?.RequestAuth(data, OnBindResponse);
			}
		}
		else
		{
			InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
		}
	}

	protected override void OnAuthLoginResponse(string error_code, SnipeObject data)
	{
		base.OnAuthLoginResponse(error_code, data);

		DebugLogger.Log($"[DeviceIdAuthProvider] {error_code}");

		if (error_code == SnipeErrorCodes.OK)
		{
			int user_id = data.SafeGetValue<int>("id");

			IsBindDone = true;

			InvokeAuthSuccessCallback(user_id);
		}
		else
		{
			InvokeAuthFailCallback(error_code);
		}
	}

	public override string GetUserId()
	{
		if (string.IsNullOrEmpty(_deviceId))
		{
			_deviceId = SystemInfo.deviceUniqueIdentifier;
			DebugLogger.Log($"[DeviceIdAuthProvider] DeviceId = {_deviceId}");
			if (_deviceId.Length > 64)
			{
				_deviceId = GetHashString(_deviceId);
				DebugLogger.Log($"[DeviceIdAuthProvider] DeviceId Hash = {_deviceId}");
			}
		}
		return _deviceId;
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

	private static byte[] GetHash(string inputString)
	{
		using (HashAlgorithm algorithm = SHA256.Create())
		{
			return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
		}
	}

	private static string GetHashString(string inputString)
	{
		StringBuilder sb = new StringBuilder();
		foreach (byte b in GetHash(inputString))
		{
			sb.Append(b.ToString("X2").ToLower());
		}

		return sb.ToString();
	}
}
