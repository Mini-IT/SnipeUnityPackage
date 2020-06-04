using System;
using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.Social;

public class AppleGameCenterAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "appl";
	public override string ProviderId { get { return PROVIDER_ID; } }

	private static Action<ExpandoObject> mLoginSignatureCallback;

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;
		
		InvokeAuthFailCallback(AuthProvider.ERROR_NOT_INITIALIZED);
	}

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		Debug.Log("[AppleGameCenterAuthProvider] RequestBind");

		mBindResultCallback = bind_callback;

		InvokeBindResultCallback(ERROR_NOT_INITIALIZED);
	}

	protected override void OnAuthLoginResponse(ExpandoObject data)
	{
		base.OnAuthLoginResponse(data);

		string error_code = data?.SafeGetString("errorCode");

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
		return AppleGameCenterProvider.InstanceInitialized ? AppleGameCenterProvider.Instance.PlayerProfile.Id : "";
	}

	public override bool CheckAuthExists(CheckAuthExistsCallback callback = null)
	{
		if (!AppleGameCenterProvider.InstanceInitialized)
			return false;

		CheckAuthExists(GetUserId(), callback);
		return true;
	}

	protected override void OnBindDone()
	{
		base.OnBindDone();

		AppleGameCenterProvider.Instance.LoggedOut += () =>
		{
			IsBindDone = false;
		};
	}
}
