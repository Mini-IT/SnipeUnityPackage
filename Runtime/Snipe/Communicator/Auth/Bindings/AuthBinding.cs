using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AuthBinding<T> : AuthBinding where T : AuthIdFetcher, new()
	{
		public AuthBinding(string provider_id) : base()
		{
			ProviderId = provider_id;
			mFetcher = new T();
		}
	}

	public class AuthBinding
	{
		public string ProviderId { get; protected set; }
	
		//public bool? AccountExists { get; protected set; } = null;

		public delegate void BindResultCallback(AuthBinding binding, string error_code);
		public delegate void CheckAuthExistsCallback(AuthBinding binding, bool exists, bool is_me, string user_name = null);

		protected BindResultCallback mBindResultCallback;

		protected AuthIdFetcher mFetcher;

		public string BindDonePrefsKey
		{
			get { return SnipePrefs.AUTH_BIND_DONE + ProviderId; }
		}

		public bool IsBindDone
		{
			get
			{
				return PlayerPrefs.GetInt(BindDonePrefsKey, 0) == 1;
			}
			internal set
			{
				SetBindDoneFlag(value, true);
			}
		}

		public AuthBinding()
		{
			if (IsBindDone)
			{
				OnBindDone();
			}
		}

		public void Start()
		{
			Debug.Log($"[AuthBinding] [{ProviderId}] Start");
			if (mFetcher != null && !IsBindDone)
			{
				Debug.Log($"[AuthBinding] [{ProviderId}] Fetch");
				mFetcher.Fetch(true, OnIdFetched);
			}
		}

		protected void OnIdFetched(string uid)
		{
			Debug.Log($"[AuthBinding] [{ProviderId}] OnIdFetched: {uid}");

			if (!string.IsNullOrEmpty(uid) && !IsBindDone)
			{
				CheckAuthExists(uid);
			}
		}

		public virtual void RequestBind(BindResultCallback bind_callback = null)
		{
			// Override this method.

			mBindResultCallback = bind_callback;

			InvokeBindResultCallback(IsBindDone ? SnipeErrorCodes.OK : SnipeErrorCodes.NOT_INITIALIZED);
		}

		public string GetUserId()
		{
			return mFetcher?.Value ?? "";
		}

		// protected override void OnAuthResetResponse(string error_code, SnipeObject response_data)
		// {
		// if (error_code == SnipeErrorCodes.NO_SUCH_AUTH)
		// {
		// AccountExists = false;
		// }

		// base.OnAuthResetResponse(error_code, response_data);
		// }

		// protected override void OnAuthLoginResponse(string error_code, SnipeObject data)
		// {
		// if (!string.IsNullOrEmpty(error_code))
		// {
		// AccountExists = (error_code == SnipeErrorCodes.OK);
		// if (AccountExists != true)
		// {
		// SetBindDoneFlag(false, false);
		// }
		// }

		// base.OnAuthLoginResponse(error_code, data);
		// }

		public void CheckAuthExists(CheckAuthExistsCallback callback = null)
		{
			if (mFetcher == null)
				return;

			mFetcher.Fetch(false, uid =>
			{
				CheckAuthExists(uid, callback);
			});
		}

		protected virtual void CheckAuthExists(string user_id, CheckAuthExistsCallback callback = null)
		{
			DebugLogger.Log($"[BindProvider] ({ProviderId}) CheckAuthExists {user_id}");

			SnipeObject data = new SnipeObject()
			{
				["ckey"] = SnipeConfig.ClientKey,
				["provider"] = ProviderId,
				["login"] = user_id,
			};

			int login_id = SnipeCommunicator.Instance.Auth.UserID;
			if (login_id != 0)
			{
				data["userID"] = login_id;
			}

			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_EXISTS)?.RequestUnauthorized(data,
				(error_code, response_data) => OnCheckAuthExistsResponse(error_code, response_data, callback));
		}

		protected virtual void OnBindResponse(string error_code, SnipeObject data)
		{
			DebugLogger.Log($"[BindProvider] ({ProviderId}) OnBindResponse - {error_code}");

			if (error_code == SnipeErrorCodes.OK)
			{
				//AccountExists = true;
				IsBindDone = true;
			}

			InvokeBindResultCallback(error_code);
		}

		protected void OnCheckAuthExistsResponse(string error_code, SnipeObject data, CheckAuthExistsCallback callback)
		{
			bool account_exists = (error_code == SnipeErrorCodes.OK);
			//if (!string.IsNullOrEmpty(error_code))
			//	AccountExists = (error_code == SnipeErrorCodes.OK);

			bool is_me = data.SafeGetValue("isSame", false);
			if (/*AccountExists == true &&*/ is_me)
				IsBindDone = SnipeCommunicator.Instance.LoggedIn;

			if (callback != null)
			{
				callback.Invoke(this, account_exists/* == true*/, is_me, data.SafeGetString("name"));
				callback = null;
			}

			if (/*AccountExists.HasValue && */SnipeCommunicator.Instance.LoggedIn)
			{
				if (!account_exists) //(AccountExists == false)
				{
					RequestBind();
				}
				else if (!is_me)
				{
					DebugLogger.Log($"[BindProvider] ({ProviderId}) OnCheckAuthExistsResponse - another account found - InvokeAccountBindingCollisionEvent");
					SnipeCommunicator.Instance.Auth.InvokeAccountBindingCollisionEvent(this, data.SafeGetString("name"));
				}
			}
		}

		protected virtual void InvokeBindResultCallback(string error_code)
		{
			DebugLogger.Log($"[BindProvider] ({ProviderId}) InvokeBindResultCallback - {error_code}");

			if (mBindResultCallback != null)
				mBindResultCallback.Invoke(this, error_code);

			mBindResultCallback = null;
		}
		
		protected void SetBindDoneFlag(bool value, bool invoke_callback)
		{
			bool current_value = PlayerPrefs.GetInt(BindDonePrefsKey, 0) == 1;
			if (value != current_value)
			{
				DebugLogger.Log($"[BindProvider] ({ProviderId}) Set bind done flag to {value}");

				PlayerPrefs.SetInt(BindDonePrefsKey, value ? 1 : 0);

				if (value && invoke_callback)
					OnBindDone();
			}
		}

		protected virtual void OnBindDone()
		{
		}

		public void DisposeCallback()
		{
			mBindResultCallback = null;
		}
	}
}