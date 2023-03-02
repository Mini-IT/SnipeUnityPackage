using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class AuthSubsystem
	{
		public delegate void AccountBindingCollisionHandler(AuthBinding binding, string user_name = null);

		public event AccountBindingCollisionHandler AccountBindingCollision;
		public event Action LoginSucceeded;
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
		
		private SnipeCommunicator _communicator;
		
		private AdvertisingIdFetcher _advertisingIdFetcher;
		private DeviceIdFetcher _deviceIdFetcher;

		private List<AuthBinding> _bindings;

		private int _loginAttempt;

		public AuthSubsystem(SnipeCommunicator communicator)
		{
			_communicator = communicator;
			_communicator.ConnectionSucceeded -= OnConnectionSucceeded;
			_communicator.ConnectionSucceeded += OnConnectionSucceeded;

			_bindings = new List<AuthBinding>()
			{
				new AdvertisingIdBinding(_communicator, this),
#if SNIPE_FACEBOOK
				new FacebookBinding(_communicator, this),
#endif
				new AmazonBinding(_communicator, this),
			};
		}

		public void Authorize()
		{
			if (_communicator == null
				|| !_communicator.Connected
				|| _communicator.LoggedIn)
			{
				return;
			}

			DebugLogger.Log($"[{GetType().Name}] ({_communicator.InstanceId}) Authorize");

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
			
			if (SnipeConfig.CompressionEnabled)
			{
				data["flagCanPack"] = true;
			}
			
			var stopwatch = Stopwatch.StartNew();

			_communicator.CreateRequest(SnipeMessageTypes.USER_LOGIN)?.RequestUnauthorized(data,
				(error_code, response) =>
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
							StartBindings();
							
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
							DebugLogger.LogError($"[{GetType().Name}] ({_communicator.InstanceId}) {SnipeMessageTypes.USER_LOGIN} - Unexpected error code: {error_code}");
							_communicator.Disconnect();
							break;
					}
				}
			);
			
			return true;
		}
		
		private void RegisterAndLogin()
		{
			if (_communicator == null)
			{
				//callback?.Invoke(false);
				return;
			}
			
			if (_advertisingIdFetcher == null)
				_advertisingIdFetcher = new AdvertisingIdFetcher();
			
			_advertisingIdFetcher.Fetch(false, adid =>
			{
				if (_deviceIdFetcher == null)
					_deviceIdFetcher = new DeviceIdFetcher();
				_deviceIdFetcher.Fetch(false, dvid =>
				{
					var providers = new List<SnipeObject>();
					
#if !BINDING_DISABLED
					if (!string.IsNullOrEmpty(adid))
					{
						providers.Add(new SnipeObject()
						{
							["provider"] = "adid",
							["login"] = adid,
						});
					}
					
					if (!string.IsNullOrEmpty(dvid))
					{
						providers.Add(new SnipeObject()
						{
							["provider"] = "dvid",
							["login"] = dvid,
						});
					}
#endif
					
					RequestRegisterAndLogin(providers);
				});
			});
		}
		
		private void RequestRegisterAndLogin(List<SnipeObject> providers)
		{
			_communicator.CreateRequest(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)?.RequestUnauthorized(
				new SnipeObject()
				{
					["version"] = SnipeClient.SNIPE_VERSION,
					["appInfo"] = SnipeConfig.AppInfo,
					["ckey"] = SnipeConfig.ClientKey,
					["auths"] = providers,
				},
				(error_code, response) =>
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

						//callback?.Invoke(true);
					}
					// else
					// {
						// callback?.Invoke(false);
					// }
				});
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

			_communicator.CreateRequest(SnipeMessageTypes.AUTH_RESTORE)?.RequestUnauthorized(
				new SnipeObject()
				{
					["ckey"] = SnipeConfig.ClientKey,
					["token"] = token,
				},
				(error_code, response) =>
				{
					if (error_code == "ok")
					{
						ClearAllBindings();
						UserID = 0;
						PlayerPrefs.SetString(SnipePrefs.AUTH_UID, response.SafeGetString("uid"));
						PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, response.SafeGetString("password"));
						PlayerPrefs.Save();
						callback?.Invoke(true);
					}
					else
					{
						callback?.Invoke(false);
					}
				});
		}

		/// <summary>
		/// Gets or creates a new instance of <c>AuthBinding</c>
		/// </summary>
		public BindingType GetBinding<BindingType>() where BindingType : AuthBinding, new()
		{
			var target_binding_type = typeof(BindingType);
			
			//if (target_binding_type == typeof(InternalAuthProvider))
			//{
			//	if (mInternalAuthProvider == null)
			//		mInternalAuthProvider = new InternalAuthProvider();
			//	return mInternalAuthProvider as ProviderType;
			//}
			
			BindingType result_binding = null;
			if (_bindings == null)
			{
				result_binding = new BindingType();
				_bindings = new List<AuthBinding>()
				{
					result_binding,
				};
			}
			else
			{
				foreach (var binding in _bindings)
				{
					if (binding != null && binding.GetType() == target_binding_type)
					{
						result_binding = binding as BindingType;
						break;
					}
				}
				
				// if no exact type match found, try base classes
				if (result_binding == null)
				{
					foreach (var binding in _bindings)
					{
						if (binding != null && binding is BindingType)
						{
							result_binding = binding as BindingType;
							break;
						}
					}
				}
			}
			
			if (result_binding == null)
			{
				result_binding = new BindingType();
			}

			return result_binding;
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
	}
}
