using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Storage;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public abstract class AuthSubsystem : IDisposable
	{
		public delegate void AccountBindingCollisionHandler(AuthBinding binding, string userName = null);

		/// <summary>
		/// The provided account identifier is already bound to another profile.
		/// <para/>For example when the user authorizes using theirs Facebook ID the server may find
		/// another account associated to this ID, meaning that the user has already played this game
		/// and theirs old account is found. In this case <see cref="AccountBindingCollision"/> event
		/// will be raised.
		/// <para/>Note: this event will not be raised if <see cref="AutomaticallyBindCollisions"/> is set to <c>true</c>
		/// </summary>
		public event AccountBindingCollisionHandler AccountBindingCollision;

		public event Action LoginRequested;
		public event Action<int> LoginSucceeded;

		/// <summary>
		/// If true then the authorization will start automatically after connection is opened.
		/// Otherwise you'll need to run <see cref="Authorize"/> method manually
		/// </summary>
		public bool AutoLogin { get; set; } = true;

		/// <summary>
		/// If set to <c>true</c> then <see cref="AccountBindingCollision"/> event will not be raised and
		/// <see cref="AuthBinding.Bind(AuthBinding.BindResultCallback)"/> will be invoked automatically.
		/// <para/>The account will be rebound to current profile
		/// </summary>
		public bool AutomaticallyBindCollisions { get; set; } = false;

		/// <summary>
		/// Indicates that a new registration is done during current session
		/// </summary>
		public bool JustRegistered { get; protected set; } = false;

		private int _userID = 0;
		public int UserID
		{
			get
			{
				if (_userID == 0)
				{
					string key = SnipePrefs.GetLoginUserID(_contextId);
					_userID = _sharedPrefs.GetInt(key, 0);
					if (_userID == 0)
					{
						// Try read a string value for backward compatibility
						string stringValue = _sharedPrefs.GetString(key);
						if (!string.IsNullOrEmpty(stringValue) && int.TryParse(stringValue, out _userID))
						{
							// resave the value as int
							_sharedPrefs.SetInt(key, _userID);
						}
					}

					if (_userID != 0)
					{
						_analytics.SetUserId(_userID.ToString());
					}
				}
				return _userID;
			}
			private set
			{
				_userID = value;
				_sharedPrefs.SetInt(SnipePrefs.GetLoginUserID(_contextId), _userID);

				_analytics.SetUserId(_userID.ToString());
			}
		}

		/// <summary>
		/// User name that is displayed in the UI and leaderboards
		/// </summary>
		public string UserName { get; protected set; }

		protected readonly ISnipeCommunicator _communicator;
		protected readonly HashSet<AuthBinding> _bindings = new ();

		protected int _loginAttempt;
		private bool _registering = false;
		private bool _reloginning = false;

		private string _authLogin;
		private string _authToken;

		private readonly int _contextId;
		protected SnipeConfig _config;
		protected readonly ISnipeAnalyticsTracker _analytics;
		protected readonly ISharedPrefs _sharedPrefs;
		protected readonly IMainThreadRunner _mainThreadRunner;
		protected readonly ILogger _logger;
		private readonly ISnipeServices _services;

		public ISnipeServices Services => _services;

		protected AuthSubsystem(int contextId, ISnipeCommunicator communicator, ISnipeAnalyticsTracker analytics, ISnipeServices services)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			_communicator = communicator;
			_communicator.ConnectionEstablished += OnConnectionEstablished;

			_contextId = contextId;
			_analytics = analytics;
			_services = services;
			_sharedPrefs = services.SharedPrefs;
			_mainThreadRunner = services.MainThreadRunner;
			_logger = services.LogService.GetLogger(nameof(AuthSubsystem));
		}

		public void Initialize(SnipeConfig config)
		{
			_config = config;
		}

		public void Authorize()
		{
			if (_communicator == null
				|| !_communicator.Connected
				|| _communicator.LoggedIn)
			{
				return;
			}

			_logger.LogTrace($"({_communicator.InstanceId}) Authorize");

			if (_bindings.Count == 0)
			{
				_logger.LogWarning("No auth bindings registered. In some projects this might be the desired behavior. But in most cases this is a bug.");
			}

			if (!LoginWithInternalAuthData())
			{
				RegisterAndLogin().Forget();
			}
		}

		protected async void DelayedAuthorize()
		{
			_loginAttempt++;
			await AlterTask.Delay(1000 * _loginAttempt);

			if (!_communicator.Connected)
				return;

			Authorize();
		}

		protected void OnConnectionEstablished()
		{
			JustRegistered = false;
			_loginAttempt = 0;

			_communicator.MessageReceived -= OnMessageReceived;
			_communicator.MessageReceived += OnMessageReceived;

			if (AutoLogin)
			{
				Authorize();
				return;
			}

			_logger.LogInformation($"Auto login is disabled. You must manually call {nameof(Authorize)}()");
		}

		private void OnMessageReceived(string messagetype, string errorcode, IDictionary<string, object> data, int requestid)
		{
			if (messagetype == SnipeMessageTypes.USER_LOGIN)
			{
				OnLogin(errorcode, data);
			}
		}

		private void OnLogin(string errorCode, IDictionary<string, object> data)
		{
			switch (errorCode)
			{
				case SnipeErrorCodes.OK:
				case SnipeErrorCodes.ALREADY_LOGGED_IN:
					OnLoginSucceeded(data);
					break;

				case SnipeErrorCodes.NO_SUCH_USER:
				case SnipeErrorCodes.LOGIN_DATA_WRONG:
					_authLogin = null;
					_authToken = null;
					string authUidKey = SnipePrefs.GetAuthUID(_contextId);
					string authKeyKey = SnipePrefs.GetAuthKey(_contextId);
					_sharedPrefs.DeleteKey(authUidKey);
					_sharedPrefs.DeleteKey(authKeyKey);
					RegisterAndLogin().Forget();
					break;

				case SnipeErrorCodes.USER_ONLINE:
				case SnipeErrorCodes.LOGOUT_IN_PROGRESS:
					if (_loginAttempt < 4)
					{
						DelayedAuthorize();
					}
					else
					{
						Disconnect();
					}
					break;

				case SnipeErrorCodes.GAME_SERVERS_OFFLINE:
				default: // unexpected error code
					_logger.LogError($"({_communicator.InstanceId}) {SnipeMessageTypes.USER_LOGIN} - Unexpected error code: {errorCode}");
					Disconnect();
					break;
			}
		}

		private void Disconnect()
		{
			_communicator.MessageReceived -= OnMessageReceived;
			_communicator.Disconnect();
		}

		private void OnLoginSucceeded(IDictionary<string, object> data)
		{
			int userId = data.SafeGetValue<int>("id");
			UserID = userId;

			if (data.TryGetValue("name", out string username))
			{
				UserName = username;
			}

			AutoLogin = true;
			_loginAttempt = 0;

			_communicator.MessageReceived -= OnMessageReceived;

			if (!_registering)
			{
				StartBindings();
				RaiseLoginSucceededEvent(userId);

				if (_reloginning)
				{
					BindAll();
					_reloginning = false;
				}
			}
		}

		protected bool LoginWithInternalAuthData()
		{
			string login = _authLogin;
			string password = _authToken;

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
			{
				string authUidKey = SnipePrefs.GetAuthUID(_contextId);
				string authKeyKey = SnipePrefs.GetAuthKey(_contextId);
				_authLogin = login = _sharedPrefs.GetString(authUidKey);
				_authToken = password = _sharedPrefs.GetString(authKeyKey);
			}

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
			{
				return false;
			}

			IDictionary<string, object> data = _config.LoginParameters != null ? new Dictionary<string, object>(_config.LoginParameters) : new Dictionary<string, object>();
			data["login"] = login;
			data["auth"] = password;
			FillCommonAuthRequestParameters(data);

			if (_config.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}

			long startTimespamp = Stopwatch.GetTimestamp();

			RunAuthRequest(() => new UnauthorizedRequest(_communicator, _services, SnipeMessageTypes.USER_LOGIN, data)
				.Request((errorCode, response) =>
				{
					var elapsed = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTimespamp);

					_analytics.TrackEvent(SnipeMessageTypes.USER_LOGIN, new Dictionary<string, object>()
					{
						["request_time"] = elapsed.TotalMilliseconds,
					});

					// See also `OnLogin` method
				})
			);

			return true;
		}

		private void FillCommonAuthRequestParameters(IDictionary<string, object> data)
		{
			data["version"] = SnipeClient.SNIPE_VERSION;
			data["appInfo"] = _config.AppInfo;
			data["flagAutoJoinRoom"] = _config.AutoJoinRoom;
			data["flagCanAck"] = true;
		}

		protected abstract UniTaskVoid RegisterAndLogin();

		protected async UniTask FetchLoginId(AuthBinding binding, Dictionary<string, Dictionary<string, object>> providers)
		{
			string provider = binding.ProviderId;
			AuthIdFetcher fetcher = binding.Fetcher;
			bool contextIdPrefix = binding.UseContextIdPrefix;

			bool done = false;

			fetcher.Fetch(false, uid =>
			{
				if (!string.IsNullOrEmpty(uid))
				{
					if (contextIdPrefix)
					{
						uid = _contextId + uid;
					}

					var providerData = new Dictionary<string, object>()
					{
						["provider"] = provider,
						["login"] = uid,
					};

					if (fetcher is IAuthIdFetcherWithToken tokenFetcher && !string.IsNullOrEmpty(tokenFetcher.Token))
					{
						providerData.Add("token", tokenFetcher.Token);
					}

					providers[provider] = providerData;
				}
				done = true;
			});

			await UniTask.WaitUntil(() => done);
		}

		protected void RequestRegisterAndLogin(Dictionary<string, Dictionary<string, object>> providers)
		{
			// Convert dictionary to list
			var providersList = providers.Values.ToList();

			IDictionary<string, object> data = _config.LoginParameters != null ? new Dictionary<string, object>(_config.LoginParameters) : new Dictionary<string, object>();
			data["ckey"] = _config.ClientKey;
			data["auths"] = providersList;
			FillCommonAuthRequestParameters(data);

			if (_config.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}

			_registering = true;

			RunAuthRequest(() => new UnauthorizedRequest(_communicator, _services, SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)
				.Request(data, (errorCode, response) =>
				{
					_registering = false;

					if (errorCode != SnipeErrorCodes.OK)
					{
						return;
					}

					string authUid = response.SafeGetString("uid");
					string authPassword = response.SafeGetString("password");
					SetAuthData(authUid, authPassword);

					JustRegistered = response.SafeGetValue<bool>("registrationDone", false);

					if (response["authsBinded"] is IList list)
					{
						for (int i = 0; i < list.Count; i++)
						{
							if (list[i] is not IDictionary<string, object> item)
							{
								continue;
							}

							string provider = item.SafeGetString("provider");
							if (string.IsNullOrEmpty(provider))
							{
								continue;
							}

							AuthBinding binding = _bindings.FirstOrDefault(b => b.ProviderId == provider);
							if (binding != null)
							{
								binding.IsBindDone = true;
							}
							else
							{
								_sharedPrefs.SetInt(SnipePrefs.GetAuthBindDone(_contextId) + provider, 1);
							}
						}
					}

					OnLoginSucceeded(response);
				})
			);
		}

		private void RunAuthRequest(Action action)
		{
			// Remove old batched requests. Otherwise they will not get renewed
			_communicator.DisposeRequests();

			bool batchMode = _communicator.BatchMode;
			_communicator.BatchMode = true;

			action.Invoke();
			LoginRequested?.Invoke();

			_communicator.BatchMode = batchMode;
		}

		private void SetAuthData(string uid, string password)
		{
			_authLogin = uid;
			_authToken = password;

			_sharedPrefs.SetString(SnipePrefs.GetAuthUID(_contextId), uid);
			_sharedPrefs.SetString(SnipePrefs.GetAuthKey(_contextId), password);
			_sharedPrefs.Save();
		}

		internal string GetInternalAuthLogin()
		{
			if (string.IsNullOrEmpty(_authLogin))
			{
				_authLogin = _sharedPrefs.GetString(SnipePrefs.GetAuthUID(_config.ContextId));
			}
			return _authLogin;
		}

		internal string GetInternalAuthToken()
		{
			if (string.IsNullOrEmpty(_authToken))
			{
				_authToken = _sharedPrefs.GetString(SnipePrefs.GetAuthKey(_config.ContextId));
			}
			return _authToken;
		}

		private void StartBindings()
		{
			_logger.LogInformation("StartBindings");

			foreach (var binding in _bindings)
			{
				binding?.Start();
			}
		}

		public void BindAll()
		{
			foreach (var binding in _bindings)
			{
				binding?.Bind();
			}
		}

		public void ClaimRestoreToken(string token, Action<bool> callback)
		{
			if (_communicator == null)
			{
				callback?.Invoke(false);
				return;
			}

			new UnauthorizedRequest(_communicator, _services, SnipeMessageTypes.AUTH_RESTORE)
				.Request(new Dictionary<string, object>()
				{
					["ckey"] = _config.ClientKey,
					["token"] = token,
				},
				(errorCode, response) =>
				{
					bool success = (errorCode == "ok");
					if (success)
					{
						ClearAllBindings();

						UserID = 0;

						string uid = response.SafeGetString("uid");
						string pwd = response.SafeGetString("password");
						SetAuthData(uid, pwd);
					}

					callback?.Invoke(success);
				});
		}

		public TBinding RegisterBinding<TBinding>(TBinding binding) where TBinding : AuthBinding
		{
			binding.Initialize(_contextId, _communicator, this, () => _config.ClientKey);
			_bindings.Add(binding);
			return binding;
		}

		public bool TryGetBinding<TBinding>(out TBinding binding) where TBinding : AuthBinding
		{
			return TryGetBinding<TBinding>(true, out binding);
		}

		public bool TryGetBinding<TBinding>(bool searchBaseClasses, out TBinding binding) where TBinding : AuthBinding
		{
			Type targetBindingType = typeof(TBinding);
			binding = null;

			if (_bindings.Count <= 0)
			{
				return false;
			}

			foreach (var registeredBinding in _bindings)
			{
				if (registeredBinding != null && registeredBinding.GetType() == targetBindingType)
				{
					binding = registeredBinding as TBinding;
					break;
				}
			}

			if (binding != null)
			{
				return true;
			}

			// if no exact type match found, try base classes

			if (!searchBaseClasses)
			{
				return false;
			}

			foreach (var registeredBinding in _bindings)
			{
				if (registeredBinding is TBinding b)
				{
					binding = b;
					break;
				}
			}

			return binding != null;
		}

		public void ClearAllBindings()
		{
			foreach (var binding in _bindings)
			{
				binding.IsBindDone = false;
			}
		}

		/// <summary>
		/// Relogin to another account referenced by <paramref name="binding"/>
		/// </summary>
		/// <param name="binding">Instance of <see cref="AuthBinding"/> that references the target account</param>
		public void ReloginTo(AuthBinding binding)
		{
			binding.ResetAuth((errorCode, authUid, authToken) =>
			{
				if (errorCode != SnipeErrorCodes.OK)
				{
					_logger.LogError($"Auth reset error: {errorCode}");
					return;
				}

				ClearAllBindings();

				UserID = 0;

				SetAuthData(authUid, authToken);

				ReloginReconnect().Forget();
			});
		}

		private async UniTaskVoid ReloginReconnect()
		{
			_communicator.Disconnect();

			await UniTask.WaitWhile(() => _communicator.Connected);
			await UniTask.Delay(1000); // Server needs some time to finish a session. One second is a recommended timeout

			_reloginning = true;
			_communicator.Start();
		}

		// Called by AuthBinding
		internal void OnAccountBindingCollision(AuthBinding binding, string username = null)
		{
			if (AutomaticallyBindCollisions)
			{
				binding.Bind();
			}
			else
			{
				AccountBindingCollision?.Invoke(binding, username);
			}
		}

		private void RaiseLoginSucceededEvent(int userId)
		{
			if (LoginSucceeded != null)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					RaiseEvent(LoginSucceeded, userId);
				});
			}
		}

		#region Safe events raising

		private SafeEventRaiser _safeEventRaiser;

		private void RaiseEvent(Delegate eventDelegate, params object[] args)
		{
			_safeEventRaiser ??= new SafeEventRaiser((handler, e) =>
			{
				_logger.LogTrace($"RaiseEvent - Error in the handler {handler?.Method?.Name}: {e}");
			});

			_safeEventRaiser.RaiseEvent(eventDelegate, args);
		}

		#endregion

		public virtual void Dispose()
		{
			_communicator.ConnectionEstablished -= OnConnectionEstablished;
			_communicator.MessageReceived -= OnMessageReceived;

			foreach (var binding in _bindings)
			{
				binding?.Dispose();
			}

			_bindings.Clear();
		}
	}
}
