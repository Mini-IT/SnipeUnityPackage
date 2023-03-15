using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MiniIT;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	public class AuthSubsystem
	{
		public delegate void AccountBindingCollisionHandler(AuthBinding binding, string user_name = null);

		public event AccountBindingCollisionHandler AccountBindingCollision;
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
		
		public bool JustRegistered { get; private set; } = false;
		
		private SnipeCommunicator _communicator;
		
		private AdvertisingIdFetcher _advertisingIdFetcher;
		private DeviceIdFetcher _deviceIdFetcher;
		
		private static List<AuthBinding> _bindings;
		
		public AuthSubsystem(SnipeCommunicator communicator)
		{
			_communicator = communicator;
			_communicator.ConnectionSucceeded -= OnConnectionSucceeded;
			_communicator.ConnectionSucceeded += OnConnectionSucceeded;

			_bindings = new List<AuthBinding>()
			{
				new AdvertisingIdBinding(),
#if SNIPE_FACEBOOK
				new FacebookBinding(),
#endif
				new AmazonBinding(),
			};
		}

		public void Authorize()
		{
			if (!LoginWithInternalAuthData())
			{
				RegisterAndLogin();
			}
		}
		
		private void OnConnectionSucceeded()
		{
			JustRegistered = false;
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
			
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.USER_LOGIN)?.RequestUnauthorized(data,
				(error_code, response) =>
				{
					stopwatch?.Stop();
					
					Analytics.TrackEvent(SnipeMessageTypes.USER_LOGIN, new SnipeObject()
						{
							["request_time"] = stopwatch?.ElapsedMilliseconds,
						});
					
					if (error_code == SnipeErrorCodes.OK)
					{
						UserID = response.SafeGetValue<int>("id");
						StartBindings();
					}
					else if (error_code == SnipeErrorCodes.NO_SUCH_USER || error_code == SnipeErrorCodes.LOGIN_DATA_WRONG)
					{
						PlayerPrefs.DeleteKey(SnipePrefs.AUTH_UID);
						PlayerPrefs.DeleteKey(SnipePrefs.AUTH_KEY);

						RegisterAndLogin();
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
			
			_communicator.CreateRequest(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)?.RequestUnauthorized(
				data,
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
				_bindings.Add(result_binding);
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