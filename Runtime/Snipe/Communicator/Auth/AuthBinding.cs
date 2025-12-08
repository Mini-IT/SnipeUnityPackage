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

	public class AuthBinding : IDisposable
	{
		public delegate void BindResultCallback(AuthBinding binding, string errorCode);
		public delegate void CheckAuthExistsCallback(AuthBinding binding, bool exists, bool isMe, string username = null);

		public string ProviderId { get; }
		public AuthIdFetcher Fetcher { get; }
		public int ContextId => _contextId;

		public bool UseContextIdPrefix { get; protected set; } = true;

		public string BindDonePrefsKey => SnipePrefs.GetAuthBindDone(_contextId) + ProviderId;

		private bool? _isBindDone = null;
		public bool IsBindDone
		{
			get => _isBindDone ??= (_sharedPrefs.GetInt(BindDonePrefsKey, 0) == 1);
			internal set => SetBindDoneFlag(value);
		}

		private BindResultCallback _bindResultCallback;
		private bool _started = false;

		private int _contextId;
		private Func<string> _getClientKeyMethod;
		protected ISnipeCommunicator _communicator;
		protected AuthSubsystem _authSubsystem;
		private readonly ISharedPrefs _sharedPrefs;
		protected readonly ILogger _logger;

		public AuthBinding(string providerId, AuthIdFetcher fetcher)
		{
			_sharedPrefs = SnipeServices.Instance.SharedPrefs;
			_logger = SnipeServices.Instance.LogService.GetLogger(GetType().Name);

			ProviderId = providerId;
			Fetcher = fetcher;
		}

		public void Initialize(int contextId,
			ISnipeCommunicator communicator,
			AuthSubsystem authSubsystem,
			Func<string> getClientKeyMethod)
		{
			_contextId = contextId;
			_communicator = communicator;
			_authSubsystem = authSubsystem;
			_getClientKeyMethod = getClientKeyMethod;
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

		private void OnIdFetched(string uid)
		{
			_logger.LogTrace($"OnIdFetched: {uid}");

			if (!string.IsNullOrEmpty(uid) && !IsBindDone)
			{
				uid = FormatUserId(uid);
				CheckAuthExists(uid);
			}
		}

		public void Bind(BindResultCallback callback = null)
		{
			_bindResultCallback = callback;

			if (IsBindDone)
			{
				InvokeBindResultCallback(SnipeErrorCodes.OK);
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
			return FormatUserId(Fetcher?.Value);
		}

		private string FormatUserId(string uid)
		{
			if (string.IsNullOrEmpty(uid))
			{
				return "";
			}

			if (UseContextIdPrefix)
			{
				uid = _contextId + uid;
			}

			return uid;
		}

		protected string GetClientKey() => _getClientKeyMethod?.Invoke();

		protected string GetInternalAuthToken()
		{
			return _authSubsystem.GetInternalAuthToken();
		}

		protected string GetInternalAuthLogin()
		{
			return _authSubsystem.GetInternalAuthLogin();
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
		/// <param name="callback">Parameters are <c>errorCode</c>, <c>authLogin</c>, <c>authToken</c> </param>
		public void ResetAuth(Action<string, string, string> callback)
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
				.Request((errorCode, responseData) =>
				{
					string authLogin = null;
					string authToken = null;

					if (errorCode == SnipeErrorCodes.OK)
					{
						authLogin = responseData.SafeGetString("uid");
						authToken = responseData.SafeGetString("password");

						// if (!string.IsNullOrEmpty(authLogin) && !string.IsNullOrEmpty(authToken))
						// {
						// 	_sharedPrefs.SetString(SnipePrefs.GetAuthUID(_contextId), authLogin);
						// 	_sharedPrefs.SetString(SnipePrefs.GetAuthKey(_contextId), authToken);
						// }

						SetBindDoneFlag(true);
					}

					callback?.Invoke(errorCode, authLogin, authToken);
				});
		}

		public void CheckAuthExists(CheckAuthExistsCallback callback = null)
		{
			Fetcher?.Fetch(false, uid =>
			{
				uid = FormatUserId(uid);
				CheckAuthExists(uid, callback);
			});
		}

		private void CheckAuthExists(string formattedExternalUserID, CheckAuthExistsCallback callback = null)
		{
			_logger.LogTrace($"({ProviderId}) CheckAuthExists {formattedExternalUserID}");

			IDictionary<string, object> data = new Dictionary<string, object>()
			{
				["ckey"] = GetClientKey(),
				["provider"] = ProviderId,
				["login"] = formattedExternalUserID,
			};

			int loginID = _authSubsystem.UserID;
			if (loginID != 0)
			{
				data["userID"] = loginID;
			}

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_EXISTS, data)
				.Request((errorCode, responseData) => OnCheckAuthExistsResponse(errorCode, responseData, callback));
		}

		private void OnBindResponse(string errorCode, IDictionary<string, object> data)
		{
			_logger.LogTrace($"({ProviderId}) OnBindResponse - {errorCode}");

			if (errorCode == SnipeErrorCodes.OK)
			{
				IsBindDone = true;
			}

			InvokeBindResultCallback(errorCode);
		}

		private void OnCheckAuthExistsResponse(string errorCode, IDictionary<string, object> data, CheckAuthExistsCallback callback)
		{
			bool accountExists = (errorCode == SnipeErrorCodes.OK);

			bool isMe = data.SafeGetValue("isSame", false);
			string username = data.SafeGetString("name");

			if (isMe)
			{
				IsBindDone = true;
			}

			if (callback != null)
			{
				callback.Invoke(this, accountExists, isMe, username);
				callback = null;
			}

			if (!_communicator.LoggedIn)
			{
				return;
			}

			if (!accountExists)
			{
				Bind();
			}
			else if (!isMe)
			{
				_logger.LogTrace($"({ProviderId}) OnCheckAuthExistsResponse - another account found - InvokeAccountBindingCollisionEvent");
				_authSubsystem.OnAccountBindingCollision(this, username);
			}
		}

		private void InvokeBindResultCallback(string errorCode)
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

		public void Dispose()
		{
			Fetcher?.Dispose();
			DisposeCallback();
		}
	}
}
