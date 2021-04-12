using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class BindProvider : AuthProvider
	{
		public bool? AccountExists { get; protected set; } = null;

		public delegate void BindResultCallback(BindProvider provider, string error_code);
		public delegate void CheckAuthExistsCallback(BindProvider provider, bool exists, bool is_me, string user_name = null);

		protected BindResultCallback mBindResultCallback;
		protected CheckAuthExistsCallback mCheckAuthExistsCallback;

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

		public BindProvider() : base()
		{
			if (IsBindDone)
				OnBindDone();
		}

		public virtual void RequestBind(BindResultCallback bind_callback = null)
		{
			// Override this method.

			mBindResultCallback = bind_callback;

			InvokeBindResultCallback(IsBindDone ? SnipeErrorCodes.OK : SnipeErrorCodes.NOT_INITIALIZED);
		}

		public virtual string GetUserId()
		{
			// Override this method.
			return "";
		}

		protected override void OnAuthLoginResponse(SnipeObject data)
		{
			base.OnAuthLoginResponse(data);

			string error_code = data?.SafeGetString("errorCode");
			if (!string.IsNullOrEmpty(error_code))
			{
				AccountExists = (error_code == SnipeErrorCodes.OK);
				if (AccountExists != true)
				{
					SetBindDoneFlag(false, false);
				}
			}
		}

		public virtual bool CheckAuthExists(CheckAuthExistsCallback callback = null)
		{
			// Override this method.
			return false;
		}

		protected virtual void CheckAuthExists(string user_id, CheckAuthExistsCallback callback)
		{
			mCheckAuthExistsCallback = callback;

			DebugLogger.Log($"[BindProvider] ({ProviderId}) CheckAuthExists {user_id}");

			SnipeObject data = new SnipeObject();
			data["messageType"] = SnipeMessageTypes.AUTH_USER_EXISTS;
			data["provider"] = ProviderId;
			data["login"] = user_id;

			string login_id = PlayerPrefs.GetString(SnipePrefs.LOGIN_USER_ID);
			if (!string.IsNullOrEmpty(login_id))
				data["id"] = Convert.ToInt32(login_id);

			SingleRequestClient.Request(SnipeConfig.Instance.AuthWebsocketURL, data, OnCheckAuthExistsResponse);
		}

		protected virtual void OnBindResponse(SnipeObject data)
		{
			string error_code = data?.SafeGetString("errorCode");

			DebugLogger.Log($"[BindProvider] ({ProviderId}) OnBindResponse - {error_code}");

			if (error_code == "ok")
			{
				AccountExists = true;
				IsBindDone = true;
			}

			InvokeBindResultCallback(error_code);
		}

		protected void OnCheckAuthExistsResponse(SnipeObject data)
		{
			string error_code = data?.SafeGetString("errorCode");
			if (!string.IsNullOrEmpty(error_code))
				AccountExists = (error_code == SnipeErrorCodes.OK);
			
			bool is_me = data.SafeGetValue("isSame", false);
			if (AccountExists == true && is_me)
				IsBindDone = true;

			if (mCheckAuthExistsCallback != null)
			{
				mCheckAuthExistsCallback.Invoke(this, AccountExists == true, is_me, data.SafeGetString("name"));
				mCheckAuthExistsCallback = null;
			}

			if (AccountExists.HasValue)
			{
				if (AccountExists == false)
				{
					RequestBind();
				}
				else if (!is_me)
				{
					DebugLogger.Log($"[BindProvider] ({ProviderId}) OnCheckAuthExistsResponse - another account found - InvokeAccountBindingCollisionEvent");
					SnipeAuthCommunicator.InvokeAccountBindingCollisionEvent(this, data.SafeGetString("name"));
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

		public override void Dispose()
		{
			mBindResultCallback = null;

			base.Dispose();
		}
	}
}