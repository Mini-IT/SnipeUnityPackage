using System;
using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.Social;

public class GooglePlayAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "goog";
	public override string ProviderId { get { return PROVIDER_ID; } }

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;

#if UNITY_ANDROID && !UNITY_EDITOR
		if (GooglePlayProvider.InstanceInitialized)
		{
			string google_login = GooglePlayProvider.Instance.PlayerProfile.Id;
			if (!string.IsNullOrEmpty(google_login))
			{
				GooglePlayProvider.Instance.GetServerAuthToken((google_token) =>
				{
					DebugLogger.Log("[GooglePlayAuthProvider] google_token : " + (string.IsNullOrEmpty(google_token) ? "empty" : "ok"));

					if (string.IsNullOrEmpty(google_token))
						InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
					else
						RequestLogin(ProviderId, google_login, google_token, reset_auth);
				});

				return;
			}
		}
		else
		{
			GooglePlayProvider.InstanceInitializationComplete -= OnGooglePlayProviderInitializationComplete;
			GooglePlayProvider.InstanceInitializationComplete += OnGooglePlayProviderInitializationComplete;
		}
#endif

		InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
	}

#if UNITY_ANDROID && !UNITY_EDITOR
	private void OnGooglePlayProviderInitializationComplete()
	{
		DebugLogger.Log("[GooglePlayAuthProvider] OnGooglePlayProviderInitializationComplete");

		GooglePlayProvider.InstanceInitializationComplete -= OnGooglePlayProviderInitializationComplete;

		if (!string.IsNullOrEmpty(SnipeCommunicator.Instance.Auth.LoginToken))
		{
			CheckAuthExists(null);
		}
	}
#endif

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		DebugLogger.Log("[GooglePlayAuthProvider] RequestBind");

		mBindResultCallback = bind_callback;
		
		if (IsBindDone)
		{
			InvokeBindResultCallback(SnipeErrorCodes.OK);
			return;
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		if (PlayerPrefs.HasKey(SnipePrefs.AUTH_UID) && PlayerPrefs.HasKey(SnipePrefs.AUTH_KEY))
		{
			if (GooglePlayProvider.InstanceInitialized)
			{
				DebugLogger.Log("[GooglePlayAuthProvider] GetServerAuthToken");

				GooglePlayProvider.Instance.GetServerAuthToken((google_token) =>
				{
					if (string.IsNullOrEmpty(google_token))
					{
						DebugLogger.Log("[GooglePlayAuthProvider] google_token is empty");
						InvokeBindResultCallback(SnipeErrorCodes.NOT_INITIALIZED);
						return;
					}

					string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
					string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

					if (string.IsNullOrEmpty(auth_login) || string.IsNullOrEmpty(auth_token))
					{
						DebugLogger.Log("[GooglePlayAuthProvider] internal uid or token is invalid");
						InvokeBindResultCallback(SnipeErrorCodes.PARAMS_WRONG);
						return;
					}

					SnipeObject data = new SnipeObject()
					{
						["provider"] = ProviderId,
						["login"] = GooglePlayProvider.Instance.PlayerProfile.Id,
						["auth"] = google_token,
						["loginInt"] = auth_login,
						["authInt"] = auth_token,
					}

					DebugLogger.Log("[GooglePlayAuthProvider] send user.bind " + data.ToJSONString());
					SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_BIND)?.RequestAuth(data, OnBindResponse);
				});

				return;
			}
		}

		GooglePlayProvider.InstanceInitializationComplete -= OnGooglePlayProviderInitializationComplete;
		GooglePlayProvider.InstanceInitializationComplete += OnGooglePlayProviderInitializationComplete;
#endif

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
		return GooglePlayProvider.InstanceInitialized ? GooglePlayProvider.Instance.PlayerProfile.Id : "";
	}

	public override bool CheckAuthExists(CheckAuthExistsCallback callback = null)
	{
		if (!GooglePlayProvider.InstanceInitialized)
			return false;

		CheckAuthExists(GetUserId(), callback);
		return true;
	}

	protected override void OnBindDone()
	{
		base.OnBindDone();

		GooglePlayProvider.Instance.LoggedOut += () =>
		{
			IsBindDone = false;
		};
	}
}
