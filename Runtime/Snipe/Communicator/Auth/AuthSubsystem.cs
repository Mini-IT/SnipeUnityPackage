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
	public abstract class AuthSubsystem
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
		public event Action LoginSucceeded;

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

		public bool UseDefaultBindings { get; set; } = true;

		private int _userID = 0;
		public int UserID
		{
			get
			{
				if (_userID == 0)
				{
					string key = SnipePrefs.GetLoginUserID(_config.ContextId);
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
			protected set
			{
				_userID = value;
				_sharedPrefs.SetInt(SnipePrefs.GetLoginUserID(_config.ContextId), _userID);

				_analytics.SetUserId(_userID.ToString());
			}
		}

		/// <summary>
		/// User name that is displayed in the UI and leaderboards
		/// </summary>
		public string UserName { get; protected set; }

		protected readonly SnipeCommunicator _communicator;
		protected readonly List<AuthBinding> _bindings;

		protected int _loginAttempt;
		private bool _registering = false;

		private string _authLogin;
		private string _authToken;

		protected readonly SnipeConfig _config;
		protected readonly SnipeAnalyticsTracker _analytics;
		protected readonly ISharedPrefs _sharedPrefs;
		protected readonly IMainThreadRunner _mainThreadRunner;
		protected readonly ILogger _logger;

		public AuthSubsystem(SnipeCommunicator communicator, SnipeConfig config)
		{
			_communicator = communicator;
			_communicator.ConnectionSucceeded += OnConnectionSucceeded;

			_config = config;
			_analytics = SnipeServices.Analytics.GetTracker(_config.ContextId);
			_sharedPrefs = SnipeServices.SharedPrefs;
			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_logger = SnipeServices.LogService.GetLogger(nameof(AuthSubsystem));

			_bindings = new List<AuthBinding>();
		}

		protected virtual void InitDefaultBindings()
		{
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

			if (UseDefaultBindings)
			{
				InitDefaultBindings();
			}

			if (!LoginWithInternalAuthData())
			{
				RegisterAndLogin();
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

		protected void OnConnectionSucceeded()
		{
			JustRegistered = false;
			_loginAttempt = 0;

			_communicator.MessageReceived -= OnMessageReceived;
			_communicator.MessageReceived += OnMessageReceived;

			if (AutoLogin)
			{
				Authorize();
			}
		}

		private void OnMessageReceived(string messagetype, string errorcode, SnipeObject data, int requestid)
		{
			if (messagetype == SnipeMessageTypes.USER_LOGIN)
			{
				OnLogin(errorcode, data);
			}
		}

		private void OnLogin(string errorCode, SnipeObject data)
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
					string authUidKey = SnipePrefs.GetAuthUID(_config.ContextId);
					string authKeyKey = SnipePrefs.GetAuthKey(_config.ContextId);
					_sharedPrefs.DeleteKey(authUidKey);
					_sharedPrefs.DeleteKey(authKeyKey);
					RegisterAndLogin();
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

		private void OnLoginSucceeded(SnipeObject data)
		{
			UserID = data.SafeGetValue<int>("id");

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
				RaiseLoginSucceededEvent();
			}
		}

		protected bool LoginWithInternalAuthData()
		{
			string login = _authLogin;
			string password = _authToken;

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
			{
				string authUidKey = SnipePrefs.GetAuthUID(_config.ContextId);
				string authKeyKey = SnipePrefs.GetAuthKey(_config.ContextId);
				_authLogin = login = _sharedPrefs.GetString(authUidKey);
				_authToken = password = _sharedPrefs.GetString(authKeyKey);
			}

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
			{
				return false;
			}

			SnipeObject data = _config.LoginParameters != null ? new SnipeObject(_config.LoginParameters) : new SnipeObject();
			data["login"] = login;
			data["auth"] = password;
			FillCommonAuthRequestParameters(data);

			if (_config.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}

			long startTimespamp = Stopwatch.GetTimestamp();

			RunAuthRequest(() => new UnauthorizedRequest(_communicator, SnipeMessageTypes.USER_LOGIN, data)
				.Request((errorCode, response) =>
				{
					var elapsed = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTimespamp);

					_analytics.TrackEvent(SnipeMessageTypes.USER_LOGIN, new SnipeObject()
					{
						["request_time"] = elapsed.TotalMilliseconds,
					});

					// See also `OnLogin` method
				})
			);

			return true;
		}

		private void FillCommonAuthRequestParameters(SnipeObject data)
		{
			data["version"] = SnipeClient.SNIPE_VERSION;
			data["appInfo"] = _config.AppInfo;
			data["flagAutoJoinRoom"] = _config.AutoJoinRoom;
			data["flagCanAck"] = true;
		}

		protected abstract void RegisterAndLogin();

		protected async UniTask FetchLoginId(string provider, AuthIdFetcher fetcher, List<SnipeObject> providers, bool contextIdPrefix)
		{
			bool done = false;

			fetcher.Fetch(false, uid =>
			{
				if (!string.IsNullOrEmpty(uid))
				{
					if (contextIdPrefix)
					{
						uid = _config.ContextId + uid;
					}

					providers.Add(new SnipeObject()
					{
						["provider"] = provider,
						["login"] = uid,
					});
				}
				done = true;
			});

			while (!done)
			{
				await AlterTask.Delay(20);
			}
		}

		protected void RequestRegisterAndLogin(List<SnipeObject> providers)
		{
			SnipeObject data = _config.LoginParameters != null ? new SnipeObject(_config.LoginParameters) : new SnipeObject();
			data["ckey"] = _config.ClientKey;
			data["auths"] = providers;
			FillCommonAuthRequestParameters(data);

			if (_config.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}

			_registering = true;

			RunAuthRequest(() => new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)
				.Request(data, (errorCode, response) =>
				{
					_registering = false;

					if (errorCode == SnipeErrorCodes.OK)
					{
						string authUid = response.SafeGetString("uid");
						string authPassword = response.SafeGetString("password");
						SetAuthData(authUid, authPassword);

						JustRegistered = response.SafeGetValue<bool>("registrationDone", false);

						if (response["authsBinded"] is IList list)
						{
							for (int i = 0; i < list.Count; i++)
							{
								var item = list[i] as SnipeObject;
								if (item != null)
								{
									string provider = item.SafeGetString("provider");
									if (!string.IsNullOrEmpty(provider))
									{
										AuthBinding binding = _bindings.FirstOrDefault(b => b.ProviderId == provider);
										if (binding != null)
										{
											binding.IsBindDone = true;
										}
										else
										{
											_sharedPrefs.SetInt(SnipePrefs.GetAuthBindDone(_config.ContextId) + provider, 1);
										}
									}
								}
							}
						}

						OnLoginSucceeded(response);
					}
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

		internal void SetAuthData(string uid, string password)
		{
			_authLogin = uid;
			_authToken = password;
			_sharedPrefs.SetString(SnipePrefs.GetAuthUID(_config.ContextId), uid);
			_sharedPrefs.SetString(SnipePrefs.GetAuthKey(_config.ContextId), password);
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

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_RESTORE)
				.Request(new SnipeObject()
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

		/// <summary>
		/// Gets or creates a new instance of <see cref="AuthBinding"/>
		/// </summary>
		public BindingType GetBinding<BindingType>(bool create = true) where BindingType : AuthBinding
		{
			BindingType resultBinding = FindBinding<BindingType>();

			if (resultBinding == null && create)
			{
				resultBinding = CreateBinding<BindingType>();
				_bindings.Add(resultBinding);
			}

			return resultBinding;
		}

		protected BindingType FindBinding<BindingType>(bool tryBaseClasses = true) where BindingType : AuthBinding
		{
			Type targetBindingType = typeof(BindingType);
			BindingType resultBinding = null;

			if (_bindings.Count > 0)
			{
				foreach (var binding in _bindings)
				{
					if (binding != null && binding.GetType() == targetBindingType)
					{
						resultBinding = binding as BindingType;
						break;
					}
				}

				// if no exact type match found, try base classes
				if (resultBinding == null && tryBaseClasses)
				{
					foreach (var binding in _bindings)
					{
						if (binding != null && binding is BindingType)
						{
							resultBinding = binding as BindingType;
							break;
						}
					}
				}
			}

			return resultBinding;
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
		/// <param name="destroyContext">Action that should gracefully destroy current <see cref="SnipeContext"/></param>
		/// <param name="startContext">Action that starts a new <see cref="SnipeContext"/></param>
		public void ReloginTo(AuthBinding binding, Action destroyContext, Action startContext)
		{
			binding.ResetAuth(async (errorCode) =>
			{
				// TODO:
				// if (errorCode != "ok") {...}

				ClearAllBindings();

				destroyContext.Invoke();

				binding.IsBindDone = false;

				UserID = 0;

				await AlterTask.Delay(1000);
				startContext.Invoke();
			});
		}

		// Called by BindProvider
		internal void OnAccountBindingCollision(AuthBinding binding, string user_name = null)
		{
			if (AutomaticallyBindCollisions)
			{
				binding.Bind();
			}
			else
			{
				AccountBindingCollision?.Invoke(binding, user_name);
			}
		}

		/// <summary>
		/// Creates an instance of <see cref="AuthBinding"/> using reflection
		/// </summary>
		/// <returns>A new instance of <c>BindingType</c></returns>
		/// <exception cref="ArgumentException">No constructor found matching required parameters types</exception>
		protected BindingType CreateBinding<BindingType>() where BindingType : AuthBinding
		{
			var constructor = typeof(BindingType).GetConstructor(new Type[] { typeof(SnipeCommunicator), typeof(AuthSubsystem), typeof(SnipeConfig) });

			if (constructor == null)
			{
				throw new ArgumentException("Unsupported contructor");
			}

			return constructor.Invoke(new object[] { _communicator, this, _config }) as BindingType;
		}

		private void RaiseLoginSucceededEvent()
		{
			if (LoginSucceeded != null)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					RaiseEvent(LoginSucceeded);
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
	}
}
