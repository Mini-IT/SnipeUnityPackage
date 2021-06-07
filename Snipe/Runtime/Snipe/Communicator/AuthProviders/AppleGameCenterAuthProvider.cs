using System;
using UnityEngine;
using MiniIT;
using MiniIT.Snipe;
using MiniIT.Social;

#if UNITY_IOS
using System.Runtime.InteropServices;
using AOT;
#endif

public class AppleGameCenterAuthProvider : BindProvider
{
	public const string PROVIDER_ID = "appl";
	public override string ProviderId { get { return PROVIDER_ID; } }

	private static Action<SnipeObject> mLoginSignatureCallback;

	public override void RequestAuth(AuthSuccessCallback success_callback, AuthFailCallback fail_callback, bool reset_auth = false)
	{
		mAuthSuccessCallback = success_callback;
		mAuthFailCallback = fail_callback;

#if UNITY_IOS
		if (AppleGameCenterProvider.InstanceInitialized)
		{
			string gc_login = AppleGameCenterProvider.Instance.PlayerProfile.Id;
			if (!string.IsNullOrEmpty(gc_login))
			{
				mLoginSignatureCallback = (data) =>
				{
					DebugLogger.Log("[AppleGameCenterAuthProvider] RequestAuth - LoginSignatureCallback");
					
					data["messageType"] = SnipeMessageTypes.AUTH_USER_LOGIN;
					data["login"] = gc_login;
					if (reset_auth)
						data["resetInternalAuth"] = reset_auth;
					
					SingleRequestClient.Request(SnipeConfig.Instance.AuthWebsocketURL, data, OnAuthLoginResponse);
				};
				generateIdentityVerificationSignature(VerificationSignatureGeneratorCallback);
				return;
			}
		}
		else
		{
			AppleGameCenterProvider.InstanceInitializationComplete -= OnAppleGameCenterProviderInitializationComplete;
			AppleGameCenterProvider.InstanceInitializationComplete += OnAppleGameCenterProviderInitializationComplete;
		}
#endif

		InvokeAuthFailCallback(SnipeErrorCodes.NOT_INITIALIZED);
	}

#if UNITY_IOS
	private void OnAppleGameCenterProviderInitializationComplete()
	{
		DebugLogger.Log("[AppleGameCenterAuthProvider] OnAppleGameCenterProviderInitializationComplete");

		AppleGameCenterProvider.InstanceInitializationComplete -= OnAppleGameCenterProviderInitializationComplete;

		if (!string.IsNullOrEmpty(SnipeCommunicator.Instance.Auth.LoginToken))
		{
			CheckAuthExists(null);
		}
	}
#endif

	public override void RequestBind(BindResultCallback bind_callback = null)
	{
		DebugLogger.Log("[AppleGameCenterAuthProvider] RequestBind");

		mBindResultCallback = bind_callback;
		
		if (IsBindDone)
		{
			InvokeBindResultCallback(SnipeErrorCodes.OK);
			return;
		}
		
#if UNITY_IOS
		if (PlayerPrefs.HasKey(SnipePrefs.AUTH_UID) && PlayerPrefs.HasKey(SnipePrefs.AUTH_KEY))
		{
			if (AppleGameCenterProvider.InstanceInitialized)
			{
				string gc_login = AppleGameCenterProvider.Instance.PlayerProfile.Id;
				if (!string.IsNullOrEmpty(gc_login))
				{
					mLoginSignatureCallback = (data) =>
					{
						DebugLogger.Log("[AppleGameCenterAuthProvider] RequestBind - LoginSignatureCallback");

						string auth_login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
						string auth_token = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

						if (string.IsNullOrEmpty(auth_login) || string.IsNullOrEmpty(auth_token))
						{
							DebugLogger.Log("[AppleGameCenterAuthProvider] internal uid or token is invalid");
							InvokeBindResultCallback(SnipeErrorCodes.PARAMS_WRONG);
							return;
						}

						data["provider"] = ProviderId;
						data["login"] = gc_login;
						//data["auth"] = login_token;
						data["loginInt"] = auth_login;
						data["authInt"] = auth_token;

						DebugLogger.Log("[AppleGameCenterAuthProvider] send user.bind " + data.ToJSONString());
						SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_BIND)?.RequestAuth(data, OnBindResponse);
						
					};
					generateIdentityVerificationSignature(VerificationSignatureGeneratorCallback);
					return;
				}

				return;
			}
		}

		AppleGameCenterProvider.InstanceInitializationComplete -= OnAppleGameCenterProviderInitializationComplete;
		AppleGameCenterProvider.InstanceInitializationComplete += OnAppleGameCenterProviderInitializationComplete;
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

#region GenerateIdentityVerificationSignature
#if UNITY_IOS
	// https://gist.github.com/BastianBlokland/bbc02a407b05beaf3f55ead3dd10f808
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void IdentityVerificationSignatureCallback(
		string publicKeyUrl,
		IntPtr signaturePointer, int signatureLength,
		IntPtr saltPointer, int saltLength,
		ulong timestamp,
		string error);

	[DllImport("__Internal")]
	private static extern void generateIdentityVerificationSignature(
		[MarshalAs(UnmanagedType.FunctionPtr)]IdentityVerificationSignatureCallback callback);

	// Note: This callback has to be static because Unity's il2Cpp doesn't support marshalling instance methods.
	[MonoPInvokeCallback(typeof(IdentityVerificationSignatureCallback))]
	private static void VerificationSignatureGeneratorCallback(
		string publicKeyUrl,
		IntPtr signaturePointer, int signatureLength,
		IntPtr saltPointer, int saltLength,
		ulong timestamp,
		string error)
	{
		// Create a managed array for the signature
		var signature = new byte[signatureLength];
		Marshal.Copy(signaturePointer, signature, 0, signatureLength);

		// Create a managed array for the salt
		var salt = new byte[saltLength];
		Marshal.Copy(saltPointer, salt, 0, saltLength);

		//UnityEngine.DebugLogger.Log($"publicKeyUrl: {publicKeyUrl}");
		//UnityEngine.DebugLogger.Log($"signature length: {signature?.Length}");
		//UnityEngine.DebugLogger.Log($"salt length: {salt?.Length}");
		//UnityEngine.DebugLogger.Log($"timestamp: {timestamp}");
		//UnityEngine.DebugLogger.Log($"error: {error}");

		if (mLoginSignatureCallback != null)
		{
			SnipeObject data = new SnipeObject();
			data["provider"] = PROVIDER_ID;
			data["publicKeyUrl"] = publicKeyUrl;
			data["signature"] = Convert.ToBase64String(signature);
			data["salt"] = Convert.ToBase64String(salt);
			data["timestamp"] = Convert.ToString(timestamp);

			mLoginSignatureCallback.Invoke(data);
		}
	}
#endif
#endregion // GenerateIdentityVerificationSignature
}
