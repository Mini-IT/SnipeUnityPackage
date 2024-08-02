using System;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.SharedPrefs;

namespace MiniIT.Snipe
{
	public class AuthBinding<FetcherType> : AuthBinding where FetcherType : AuthIdFetcher, new()
	{
		public AuthBinding(string provider_id, SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base(provider_id, new FetcherType(), communicator, authSubsystem, config)
		{
		}
	}

	public class AuthBinding
	{
		public delegate void BindResultCallback(AuthBinding binding, string error_code);
		public delegate void CheckAuthExistsCallback(AuthBinding binding, bool exists, bool is_me, string user_name = null);

		public string ProviderId { get; }
		public AuthIdFetcher Fetcher { get; }

		//public bool? AccountExists { get; protected set; } = null;

		public string BindDonePrefsKey => SnipePrefs.GetAuthBindDone(_config.ContextId) + ProviderId;

		private bool? _isBindDone = null;
		public bool IsBindDone
		{
			get => _isBindDone ??= (_sharedPrefs.GetInt(BindDonePrefsKey, 0) == 1);
			internal set => SetBindDoneFlag(value);
		}

		protected BindResultCallback _bindResultCallback;
		private bool _started = false;

		protected readonly SnipeCommunicator _communicator;
		private readonly AuthSubsystem _authSubsystem;
		private readonly SnipeConfig _config;
		private readonly ISharedPrefs _sharedPrefs;
		private readonly ILogger _logger;

		public AuthBinding(string provider_id,
			AuthIdFetcher fetcher,
			SnipeCommunicator communicator,
			AuthSubsystem authSubsystem,
			SnipeConfig config)
		{
			_communicator = communicator;
			_authSubsystem = authSubsystem;
			_config = config;

			_sharedPrefs = SnipeServices.SharedPrefs;
			_logger = SnipeServices.LogService.GetLogger(GetType().Name);

			ProviderId = provider_id;
			Fetcher = fetcher;
		}

		public void Start()
		{
			if (_started)
			{
				_logger.LogTrace("Start - Already started");
				return;
			}
			_started = true;
			
			_logger.LogTrace("Start");
			if (Fetcher != null && !IsBindDone)
			{
				_logger.LogTrace("Fetch");
				Fetcher.Fetch(true, OnIdFetched);
			}
		}

		protected void OnIdFetched(string uid)
		{
			_logger.LogTrace($"OnIdFetched: {uid}");

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

			string auth_login = _sharedPrefs.GetString(SnipePrefs.GetAuthUID(_config.ContextId));
			string auth_token = _sharedPrefs.GetString(SnipePrefs.GetAuthKey(_config.ContextId));
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

				_logger.LogTrace($"({ProviderId}) send user.bind " + data.ToJSONString());
				new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_BIND, data)
					.Request(OnBindResponse);

				return;
			}
		}

		public string GetUserId()
		{
			return GetContextBoundUserId(Fetcher?.Value ?? "");
		}

		private string GetContextBoundUserId(string uid)
		{
			return _config.ContextId + uid;
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
							_sharedPrefs.SetString(SnipePrefs.GetAuthUID(_config.ContextId), auth_login);
							_sharedPrefs.SetString(SnipePrefs.GetAuthKey(_config.ContextId), auth_token);
						}

						SetBindDoneFlag(true);
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
			user_id = GetContextBoundUserId(user_id);

			_logger.LogTrace($"({ProviderId}) CheckAuthExists {user_id}");

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
			_logger.LogTrace($"({ProviderId}) OnBindResponse - {error_code}");

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
			//	AccountExists = (error_code == SnipeErrorCodes.OK);

			bool is_me = data.SafeGetValue("isSame", false);
			if (/*AccountExists == true &&*/ is_me)
			{
				IsBindDone = true; // _communicator.LoggedIn;
			}

			if (callback != null)
			{
				callback.Invoke(this, account_exists, is_me, data.SafeGetString("name"));
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
					_logger.LogTrace($"({ProviderId}) OnCheckAuthExistsResponse - another account found - InvokeAccountBindingCollisionEvent");
					_authSubsystem.OnAccountBindingCollision(this, data.SafeGetString("name"));
				}
			}
		}

		protected virtual void InvokeBindResultCallback(string error_code)
		{
			_logger.LogTrace($"({ProviderId}) InvokeBindResultCallback - {error_code}");

			if (_bindResultCallback != null)
			{
				_bindResultCallback.Invoke(this, error_code);
				_bindResultCallback = null;
			}
		}
		
		private void SetBindDoneFlag(bool value)
		{
			if (value == IsBindDone)
			{
				return;
			}

			_logger.LogTrace($"({ProviderId}) Set bind done flag to {value}");

			_isBindDone = value;
			if (value)
			{
				_sharedPrefs.SetInt(BindDonePrefsKey, 1);
			}
			else
			{
				_sharedPrefs.DeleteKey(BindDonePrefsKey);
			}
		}

		public void DisposeCallback()
		{
			_bindResultCallback = null;
		}
	}
}
