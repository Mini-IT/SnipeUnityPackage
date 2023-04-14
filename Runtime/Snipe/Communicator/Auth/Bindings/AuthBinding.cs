using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AuthBinding<FetcherType> : AuthBinding where FetcherType : AuthIdFetcher, new()
	{
		public AuthBinding(string provider_id, SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base(communicator, authSubsystem, config)
		{
			ProviderId = provider_id;
			Fetcher = new FetcherType();
		}
	}

	public class AuthBinding
	{
		public delegate void BindResultCallback(AuthBinding binding, string error_code);
		public delegate void CheckAuthExistsCallback(AuthBinding binding, bool exists, bool is_me, string user_name = null);

		public string ProviderId { get; protected set; }
		public AuthIdFetcher Fetcher { get; protected set; }

		//public bool? AccountExists { get; protected set; } = null;

		public string BindDonePrefsKey => SnipePrefs.AuthBindDone(_config.ContextId) + ProviderId;

		public bool IsBindDone
		{
			get
			{
				return SharedPrefs.GetInt(BindDonePrefsKey, 0) == 1;
			}
			internal set
			{
				SetBindDoneFlag(value, true);
			}
		}

		protected BindResultCallback _bindResultCallback;

		protected readonly SnipeCommunicator _communicator;
		private readonly AuthSubsystem _authSubsystem;
		private readonly SnipeConfig _config;

		public AuthBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
		{
			_communicator = communicator;
			_authSubsystem = authSubsystem;
			_config = config;

			if (IsBindDone)
			{
				OnBindDone();
			}
		}

		public void Start()
		{
			Debug.Log($"[AuthBinding] [{ProviderId}] Start");
			if (Fetcher != null && !IsBindDone)
			{
				Debug.Log($"[AuthBinding] [{ProviderId}] Fetch");
				Fetcher.Fetch(true, OnIdFetched);
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

		public void Bind(BindResultCallback callback = null)
		{
			_bindResultCallback = callback;

			if (IsBindDone)
			{
				InvokeBindResultCallback(SnipeErrorCodes.OK);// IsBindDone ? SnipeErrorCodes.OK : SnipeErrorCodes.NOT_INITIALIZED);
				return;
			}

			string auth_login = SharedPrefs.GetString(SnipePrefs.AuthUID(_config.ContextId));
			string auth_token = SharedPrefs.GetString(SnipePrefs.AuthKey(_config.ContextId));
			string uid = GetUserId();

			if (!string.IsNullOrEmpty(auth_login) && !string.IsNullOrEmpty(auth_token) && !string.IsNullOrEmpty(uid))
			{
				SnipeObject data = new SnipeObject()
				{
					["ckey"] = _config.ClientKey,
					["provider"] = ProviderId,
					["login"] = uid,
					["loginInt"] = auth_login,
					["authInt"] = auth_token,
				};

				var pass = GetAuthPassword();
				if (!string.IsNullOrEmpty(pass))
					data["auth"] = pass;

				DebugLogger.Log($"[AuthBinding] ({ProviderId}) send user.bind " + data.ToJSONString());
				new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_BIND, data)
					.Request(OnBindResponse);

				return;
			}
		}

		public string GetUserId()
		{
			return Fetcher?.Value ?? "";
		}

		protected virtual string GetAuthPassword()
		{
			return "";
		}

		/// <summary>
		/// Resets stored authorization data to this account.
		/// </summary>
		/// <param name="callback">Parameter is <c>errorCode</c></param>
		public void ResetAuth(Action<string> callback)
		{
			SnipeObject data = new SnipeObject()
			{
				["ckey"] = _config.ClientKey,
				["provider"] = ProviderId,
				["login"] = GetUserId(),
				["auth"] = GetAuthPassword(),
			};

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_RESET, data)
				.Request((string error_code, SnipeObject response_data) =>
				{
					if (error_code == SnipeErrorCodes.OK)
					{
						string auth_login = response_data.SafeGetString("uid");
						string auth_token = response_data.SafeGetString("password");
						
						if (!string.IsNullOrEmpty(auth_login) && !string.IsNullOrEmpty(auth_token))
						{
							SharedPrefs.SetString(SnipePrefs.AuthUID(_config.ContextId), auth_login);
							SharedPrefs.SetString(SnipePrefs.AuthKey(_config.ContextId), auth_token);
						}
					}
					
					callback?.Invoke(error_code);
				});
		}

		public void CheckAuthExists(CheckAuthExistsCallback callback = null)
		{
			Fetcher?.Fetch(false, uid =>
			{
				CheckAuthExists(uid, callback);
			});
		}

		protected virtual void CheckAuthExists(string user_id, CheckAuthExistsCallback callback = null)
		{
			DebugLogger.Log($"[AuthBinding] ({ProviderId}) CheckAuthExists {user_id}");

			SnipeObject data = new SnipeObject()
			{
				["ckey"] = _config.ClientKey,
				["provider"] = ProviderId,
				["login"] = user_id,
			};

			int login_id = _authSubsystem.UserID;
			if (login_id != 0)
			{
				data["userID"] = login_id;
			}

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_EXISTS, data)
				.Request((error_code, response_data) => OnCheckAuthExistsResponse(error_code, response_data, callback));
		}

		protected virtual void OnBindResponse(string error_code, SnipeObject data)
		{
			DebugLogger.Log($"[AuthBinding] ({ProviderId}) OnBindResponse - {error_code}");

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
			{
				IsBindDone = _communicator.LoggedIn;
			}

			if (callback != null)
			{
				callback.Invoke(this, account_exists/* == true*/, is_me, data.SafeGetString("name"));
				callback = null;
			}

			if (/*AccountExists.HasValue && */_communicator.LoggedIn)
			{
				if (!account_exists) //(AccountExists == false)
				{
					Bind();
				}
				else if (!is_me)
				{
					DebugLogger.Log($"[AuthBinding] ({ProviderId}) OnCheckAuthExistsResponse - another account found - InvokeAccountBindingCollisionEvent");
					_authSubsystem.OnAccountBindingCollision(this, data.SafeGetString("name"));
				}
			}
		}

		protected virtual void InvokeBindResultCallback(string error_code)
		{
			DebugLogger.Log($"[AuthBinding] ({ProviderId}) InvokeBindResultCallback - {error_code}");

			if (_bindResultCallback != null)
			{
				_bindResultCallback.Invoke(this, error_code);
				_bindResultCallback = null;
			}
		}
		
		protected void SetBindDoneFlag(bool value, bool invoke_callback)
		{
			bool current_value = SharedPrefs.GetInt(BindDonePrefsKey, 0) == 1;
			if (value != current_value)
			{
				DebugLogger.Log($"[AuthBinding] ({ProviderId}) Set bind done flag to {value}");

				SharedPrefs.SetInt(BindDonePrefsKey, value ? 1 : 0);

				if (value && invoke_callback)
				{
					OnBindDone();
				}
			}
		}

		protected virtual void OnBindDone()
		{
		}

		public void DisposeCallback()
		{
			_bindResultCallback = null;
		}
	}
}
