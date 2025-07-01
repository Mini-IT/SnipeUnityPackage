using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.Storage;

namespace MiniIT.Snipe
{
	public class AuthBinding<TFetcher> : AuthBinding where TFetcher : AuthIdFetcher, new()
	{
		public AuthBinding(string providerId)
			: base(providerId, new TFetcher())
		{
		}
	}

	public class AuthBinding
	{
		public delegate void BindResultCallback(AuthBinding binding, string errorCode);
		public delegate void CheckAuthExistsCallback(AuthBinding binding, bool exists, bool isMe, string username = null);

		public string ProviderId { get; }
		public AuthIdFetcher Fetcher { get; }

		//public bool? AccountExists { get; protected set; } = null;

		public string BindDonePrefsKey => SnipePrefs.GetAuthBindDone(_contextId) + ProviderId;

		private bool? _isBindDone = null;
		public bool IsBindDone
		{
			get => _isBindDone ??= (_sharedPrefs.GetInt(BindDonePrefsKey, 0) == 1);
			internal set => SetBindDoneFlag(value);
		}

		protected BindResultCallback _bindResultCallback;
		private bool _started = false;

		protected int _contextId;
		protected Func<string> _getClientKey;
		protected SnipeCommunicator _communicator;
		protected AuthSubsystem _authSubsystem;
		private readonly ISharedPrefs _sharedPrefs;
		private readonly ILogger _logger;

		public AuthBinding(string providerId, AuthIdFetcher fetcher)
		{
			_sharedPrefs = SnipeServices.SharedPrefs;
			_logger = SnipeServices.LogService.GetLogger(GetType().Name);

			ProviderId = providerId;
			Fetcher = fetcher;
		}

		public void Initialize(int contextId,
			SnipeCommunicator communicator,
			AuthSubsystem authSubsystem,
			Func<string> getClientKeyMethod)
		{
			_contextId = contextId;
			_communicator = communicator;
			_authSubsystem = authSubsystem;
			_getClientKey = getClientKeyMethod;
		}

		public void Start()
		{
			if (_communicator == null)
			{
				throw new InvalidOperationException("Not initialized");
			}

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
			string authLogin = GetInternalAuthLogin();
			string authToken = GetInternalAuthToken();
			string uid = GetUserId();

			if (string.IsNullOrEmpty(authLogin) ||
				string.IsNullOrEmpty(authToken) ||
				string.IsNullOrEmpty(uid))
			{
				return;
			}

			var data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = uid,
				["loginInt"] = authLogin,
				["authInt"] = authToken,
			};

			string pass = GetAuthToken();
			if (!string.IsNullOrEmpty(pass))
			{
				data["auth"] = pass;
			}
			else
			{
				_logger.LogTrace($"({ProviderId}) user.bind - no auth token is used");
			}

			FillExtraParameters(data);

			_logger.LogTrace($"({ProviderId}) send user.bind " + JsonUtility.ToJson(data));
			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_BIND, data)
				.Request(OnBindResponse);
		}

		public string GetUserId()
		{
			return Fetcher?.Value ?? "";
		}

		protected string GetClientKey() => _getClientKey?.Invoke();

		protected string GetInternalAuthToken()
		{
			return _sharedPrefs.GetString(SnipePrefs.GetAuthKey(_contextId));
		}

		protected string GetInternalAuthLogin()
		{
			return _sharedPrefs.GetString(SnipePrefs.GetAuthUID(_contextId));
		}

		protected virtual string GetAuthToken()
		{
			return "";
		}

		protected virtual void FillExtraParameters(IDictionary<string, object> data)
		{
		}

		/// <summary>
		/// Resets stored authorization data to this account.
		/// </summary>
		/// <param name="callback">Parameter is <c>errorCode</c></param>
		public void ResetAuth(Action<string> callback)
		{
			IDictionary<string, object> data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = GetUserId(),
				["auth"] = GetAuthToken(),
			};

			FillExtraParameters(data);

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_RESET, data)
				.Request((string errorCode, IDictionary<string, object> responseData) =>
				{
					if (errorCode == SnipeErrorCodes.OK)
					{
						string authLogin = responseData.SafeGetString("uid");
						string authToken = responseData.SafeGetString("password");

						if (!string.IsNullOrEmpty(authLogin) && !string.IsNullOrEmpty(authToken))
						{
							_sharedPrefs.SetString(SnipePrefs.GetAuthUID(_contextId), authLogin);
							_sharedPrefs.SetString(SnipePrefs.GetAuthKey(_contextId), authToken);
						}

						SetBindDoneFlag(true);
					}

					callback?.Invoke(errorCode);
				});
		}

		public void CheckAuthExists(CheckAuthExistsCallback callback = null)
		{
			Fetcher?.Fetch(false, uid =>
			{
				CheckAuthExists(uid, callback);
			});
		}

		protected virtual void CheckAuthExists(string userID, CheckAuthExistsCallback callback = null)
		{
			_logger.LogTrace($"({ProviderId}) CheckAuthExists {userID}");

			IDictionary<string, object> data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = userID,
			};

			int loginID = _authSubsystem.UserID;
			if (loginID != 0)
			{
				data["userID"] = loginID;
			}

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_EXISTS, data)
				.Request((errorCode, responseData) => OnCheckAuthExistsResponse(errorCode, responseData, callback));
		}

		protected virtual void OnBindResponse(string errorCode, IDictionary<string, object> data)
		{
			_logger.LogTrace($"({ProviderId}) OnBindResponse - {errorCode}");

			if (errorCode == SnipeErrorCodes.OK)
			{
				//AccountExists = true;
				IsBindDone = true;
			}

			InvokeBindResultCallback(errorCode);
		}

		protected void OnCheckAuthExistsResponse(string errorCode, IDictionary<string, object> data, CheckAuthExistsCallback callback)
		{
			bool accountExists = (errorCode == SnipeErrorCodes.OK);
			//	AccountExists = (error_code == SnipeErrorCodes.OK);

			bool isMe = data.SafeGetValue("isSame", false);
			if (/*AccountExists == true &&*/ isMe)
			{
				IsBindDone = true; // _communicator.LoggedIn;
			}

			if (callback != null)
			{
				callback.Invoke(this, accountExists, isMe, data.SafeGetString("name"));
				callback = null;
			}

			if (/*AccountExists.HasValue && */_communicator.LoggedIn)
			{
				if (!accountExists) //(AccountExists == false)
				{
					Bind();
				}
				else if (!isMe)
				{
					_logger.LogTrace($"({ProviderId}) OnCheckAuthExistsResponse - another account found - InvokeAccountBindingCollisionEvent");
					_authSubsystem.OnAccountBindingCollision(this, data.SafeGetString("name"));
				}
			}
		}

		protected virtual void InvokeBindResultCallback(string errorCode)
		{
			_logger.LogTrace($"({ProviderId}) InvokeBindResultCallback - {errorCode}");

			if (_bindResultCallback != null)
			{
				_bindResultCallback.Invoke(this, errorCode);
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
