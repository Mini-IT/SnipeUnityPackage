using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AuthSubsystem
	{
		public delegate void AccountBindingCollisionHandler(AuthBinding binding, string user_name = null);

		/// <summary>
		/// The provided account identifier is already bound to another profile.
		/// <para/>For example when the user authorizes using theirs Facebook ID the server may find
		/// another account associated to this ID, meaning that the user has already played this game
		/// and theirs old account is found. In this case <see cref="AccountBindingCollision"/> event
		/// will be rased.
		/// <para/>Note: this event will not be raised if <see cref="AutomaticallyBindCollisions"/> is set to <c>true</c>
		/// </summary>
		public event AccountBindingCollisionHandler AccountBindingCollision;

		public event Action LoginRequested;
		public event Action LoginSucceeded;

		/// <summary>
		/// If set to <c>true</c> then <see cref="AccountBindingCollision"/> event will not be raised and
		/// <see cref="AuthBinding.Bind(AuthBinding.BindResultCallback)"/> will be invoked automatically.
		/// <para/>The account will be rebound to current profile
		/// </summary>
		public bool AutomaticallyBindCollisions = false;
				
		private int _userID = 0;
		public int UserID
		{
			get
			{
				if (_userID <= 0)
				{
					_userID = Convert.ToInt32(PlayerPrefs.GetString(SnipePrefs.LOGIN_USER_ID, "0"));

					if (_userID != 0)
					{
						Analytics.SetUserId(_userID.ToString());
					}
				}
				return _userID;
			}
			private set
			{
				_userID = value;
				PlayerPrefs.SetString(SnipePrefs.LOGIN_USER_ID, _userID.ToString());
				
				Analytics.SetUserId(_userID.ToString());
			}
		}

		/// <summary>
		/// User name that is displayed in the UI and leaderboards
		/// </summary>
		public string UserName { get; private set; }

		/// <summary>
		/// If true then the authorization will start automatically after connection is opened.
		/// Otherwise you'll need to run <see cref="Authorize"/> method manually
		/// </summary>
		public bool AutoLogin { get; set; } = true;

		/// <summary>
		/// Indicates that a new registration is done during current session
		/// </summary>
		public bool JustRegistered { get; private set; } = false;
		
		private readonly SnipeCommunicator _communicator;
		
		private AdvertisingIdFetcher _advertisingIdFetcher;
		private DeviceIdFetcher _deviceIdFetcher;

		private List<AuthBinding> _bindings;

		private int _loginAttempt;

		private readonly TaskScheduler _mainThreadScheduler;

		public AuthSubsystem(SnipeCommunicator communicator)
		{
			_communicator = communicator;
			_communicator.ConnectionSucceeded += OnConnectionSucceeded;

			_mainThreadScheduler = (SynchronizationContext.Current != null) ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;

			_bindings = new List<AuthBinding>()
			{
				new AdvertisingIdBinding(_communicator, this),
#if SNIPE_FACEBOOK
				new FacebookBinding(_communicator, this),
#endif
				new AmazonBinding(_communicator, this),
			};
		}

		private void RunInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}

		public void Authorize()
		{
			if (_communicator == null
				|| !_communicator.Connected
				|| _communicator.LoggedIn)
			{
				return;
			}

			DebugLogger.Log($"[{nameof(AuthSubsystem)}] ({_communicator.InstanceId}) Authorize");

			if (!LoginWithInternalAuthData())
			{
				RegisterAndLogin();
			}
		}

		private async void DelayedAuthorize()
		{
			_loginAttempt++;
			await Task.Delay(1000 * _loginAttempt);

			if (!_communicator.Connected)
				return;

			Authorize();
		}

		private void OnConnectionSucceeded()
		{
			JustRegistered = false;
			_loginAttempt = 0;

			if (AutoLogin)
			{
				Authorize();
			}
		}
		
		private bool LoginWithInternalAuthData()
		{
			string login = PlayerPrefs.GetString(SnipePrefs.AUTH_UID);
			string password = PlayerPrefs.GetString(SnipePrefs.AUTH_KEY);

			if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
			{
				return false;
			}
			
			SnipeObject data = SnipeConfig.LoginParameters != null ? new SnipeObject(SnipeConfig.LoginParameters) : new SnipeObject();
			data["login"] = login;
			data["auth"] = password;
			data["version"] = SnipeClient.SNIPE_VERSION;
			data["appInfo"] = SnipeConfig.AppInfo;
			data["flagAutoJoinRoom"] = true;
			
			if (SnipeConfig.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}
			
			var stopwatch = Stopwatch.StartNew();

			RunAuthRequest(() => new UnauthorizedRequest(_communicator, SnipeMessageTypes.USER_LOGIN, data)
				.Request((error_code, response) =>
				{
					stopwatch?.Stop();

					Analytics.TrackEvent(SnipeMessageTypes.USER_LOGIN, new SnipeObject()
					{
						["request_time"] = stopwatch?.ElapsedMilliseconds,
					});

					switch (error_code)
					{
						case SnipeErrorCodes.OK:
						case SnipeErrorCodes.ALREADY_LOGGED_IN:
							UserID = response.SafeGetValue<int>("id");
#if !BINDING_DISABLED
							StartBindings();
#endif

							UserName = data.SafeGetString("name");
							AutoLogin = true;
							_loginAttempt = 0;

							if (LoginSucceeded != null)
							{
								RunInMainThread(() =>
								{
									RaiseEvent(LoginSucceeded);
								});
							}
							break;

						case SnipeErrorCodes.NO_SUCH_USER:
						case SnipeErrorCodes.LOGIN_DATA_WRONG:
							PlayerPrefs.DeleteKey(SnipePrefs.AUTH_UID);
							PlayerPrefs.DeleteKey(SnipePrefs.AUTH_KEY);
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
								_communicator.Disconnect();
							}
							break;

						case SnipeErrorCodes.GAME_SERVERS_OFFLINE:
						default: // unexpected error code
							DebugLogger.LogError($"[{nameof(AuthSubsystem)}] ({_communicator.InstanceId}) {SnipeMessageTypes.USER_LOGIN} - Unexpected error code: {error_code}");
							_communicator.Disconnect();
							break;
					}
				})
			);

			return true;
		}
		
		private async void RegisterAndLogin()
		{
			if (_communicator == null)
			{
				return;
			}

			var providers = new List<SnipeObject>();

#if !BINDING_DISABLED
			_advertisingIdFetcher ??= new AdvertisingIdFetcher();
			_deviceIdFetcher ??= new DeviceIdFetcher();

			await Task.WhenAll(new []
			{
				FetchLoginId("adid", _advertisingIdFetcher, providers),
				FetchLoginId("dvid", _deviceIdFetcher, providers)
			});
#endif

			RequestRegisterAndLogin(providers);
		}

		private async Task FetchLoginId(string provider, AuthIdFetcher fetcher, List<SnipeObject> providers)
		{
			bool done = false;

			fetcher.Fetch(false, uid =>
			{
				if (!string.IsNullOrEmpty(uid))
				{
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
				await Task.Delay(20);
			}
		}
		
		private void RequestRegisterAndLogin(List<SnipeObject> providers)
		{
			SnipeObject data = SnipeConfig.LoginParameters != null ? new SnipeObject(SnipeConfig.LoginParameters) : new SnipeObject();
			data["version"] = SnipeClient.SNIPE_VERSION;
			data["appInfo"] = SnipeConfig.AppInfo;
			data["ckey"] = SnipeConfig.ClientKey;
			data["auths"] = providers;
			data["flagAutoJoinRoom"] = true;
			
			if (SnipeConfig.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}

			RunAuthRequest(() => new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)
				.Request(data, (error_code, response) =>
				{
					if (error_code == SnipeErrorCodes.OK)
					{
						//ClearAllBindings();
						UserID = response.SafeGetValue<int>("id");
						SetAuthData(response.SafeGetString("uid"), response.SafeGetString("password"));

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
										PlayerPrefs.SetInt(SnipePrefs.AUTH_BIND_DONE + provider, 1);
									}
								}
							}
						}

#if !BINDING_DISABLED
						StartBindings();
#endif

						if (LoginSucceeded != null)
						{
							RunInMainThread(() =>
							{
								RaiseEvent(LoginSucceeded);
							});
						}
					}
				})
			);
		}

		private void RunAuthRequest(Action action)
		{
			bool batchMode = _communicator.BatchMode;
			_communicator.BatchMode = true;

			action.Invoke();
			LoginRequested?.Invoke();

			_communicator.BatchMode = batchMode;
		}
		
		private void SetAuthData(string uid, string password)
		{
			PlayerPrefs.SetString(SnipePrefs.AUTH_UID, uid);
			PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, password);
			PlayerPrefs.Save();
		}

		private void StartBindings()
		{
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
					["ckey"] = SnipeConfig.ClientKey,
					["token"] = token,
				},
				(errorCode, response) =>
				{
					bool success = (errorCode == "ok");
					if (success)
					{
						ClearAllBindings();
						UserID = 0;
						SetAuthData(response.SafeGetString("uid"), response.SafeGetString("password"));
					}
					
					callback?.Invoke(success);
				});
		}

		/// <summary>
		/// Gets or creates a new instance of <see cref="AuthBinding"/>
		/// </summary>
		public BindingType GetBinding<BindingType>() where BindingType : AuthBinding
		{
			var targetBindingType = typeof(BindingType);

			//if (targetBindingType == typeof(InternalAuthProvider))
			//{
			//	if (mInternalAuthProvider == null)
			//		mInternalAuthProvider = new InternalAuthProvider();
			//	return mInternalAuthProvider as ProviderType;
			//}

			BindingType resultBinding = null;
			if (_bindings == null)
			{

				resultBinding = CreateBinding<BindingType>();
				_bindings = new List<AuthBinding>()
				{
					resultBinding,
				};
			}
			else
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
				if (resultBinding == null)
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
			
			if (resultBinding == null)
			{
				resultBinding = CreateBinding<BindingType>();
				_bindings.Add(resultBinding);
			}

			return resultBinding;
		}

		public void ClearAllBindings()
		{
			PlayerPrefs.DeleteKey(SnipePrefs.AUTH_BIND_DONE + "dvid");

			if (_bindings != null)
			{
				foreach (var binding in _bindings)
				{
					PlayerPrefs.DeleteKey(binding.BindDonePrefsKey);
				}
			}
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
		private BindingType CreateBinding<BindingType>() where BindingType : AuthBinding
		{
			var constructor = typeof(BindingType).GetConstructor(new Type[] { typeof(SnipeCommunicator), typeof(AuthSubsystem) });

			if (constructor == null)
			{
				throw new ArgumentException("Unsupported contructor");
			}

			return constructor.Invoke(new object[] { _communicator, this }) as BindingType;
		}

		#region Safe events raising

		// https://www.codeproject.com/Articles/36760/C-events-fundamentals-and-exception-handling-in-mu#exceptions

		private void RaiseEvent(Delegate event_delegate, params object[] args)
		{
			if (event_delegate != null)
			{
				foreach (Delegate handler in event_delegate.GetInvocationList())
				{
					if (handler == null)
						continue;

					try
					{
						handler.DynamicInvoke(args);
					}
					catch (Exception e)
					{
						string message = (e is System.Reflection.TargetInvocationException tie) ?
							$"{tie.InnerException?.Message}\n{tie.InnerException?.StackTrace}" :
							$"{e.Message}\n{e.StackTrace}";
						DebugLogger.Log($"[{nameof(AuthSubsystem)}] RaiseEvent - Error in the handler {handler?.Method?.Name}: {message}");
					}
				}
			}
		}

		#endregion
	}
}
