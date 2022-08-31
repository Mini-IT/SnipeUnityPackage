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
				
		private int mUserID = 0;
		public int UserID
		{
			get
			{
				if (mUserID <= 0)
				{
					mUserID = Convert.ToInt32(PlayerPrefs.GetString(SnipePrefs.LOGIN_USER_ID, "0"));

					if (mUserID != 0)
					{
						Analytics.SetUserId(mUserID.ToString());
					}
				}
				return mUserID;
			}
			private set
			{
				mUserID = value;
				PlayerPrefs.SetString(SnipePrefs.LOGIN_USER_ID, mUserID.ToString());
				
				Analytics.SetUserId(mUserID.ToString());
			}
		}
		
		public bool JustRegistered { get; private set; } = false;
		
		private SnipeCommunicator mCommunicator;
		
		private AdvertisingIdFetcher mAdvertisingIdFetcher;
		private DeviceIdFetcher mDeviceIdFetcher;
		
		private static List<AuthBinding> mBindings;
		
		public AuthSubsystem(SnipeCommunicator communicator)
		{
			mCommunicator = communicator;

			mBindings = new List<AuthBinding>()
			{
				new AuthBinding<AdvertisingIdFetcher>("adid"),
				new AuthBinding<FacebookIdFetcher>("fb"),
				new AmazonIdBinding(), //new AuthBinding<AmazonIdFetcher>("amzn"),
			};
		}

		public void Authorize()
		{
			if (!LoginWithInternalAuthData())
			{
				RegisterAndLogin();
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
					else if (error_code == SnipeErrorCodes.NO_SUCH_USER)
					{
						PlayerPrefs.DeleteKey(SnipePrefs.AUTH_UID);
						PlayerPrefs.DeleteKey(SnipePrefs.AUTH_KEY);
					}
				}
			);
			
			return true;
		}
		
		private void RegisterAndLogin()
		{
			if (mCommunicator == null)
			{
				//callback?.Invoke(false);
				return;
			}
			
			if (mAdvertisingIdFetcher == null)
				mAdvertisingIdFetcher = new AdvertisingIdFetcher();
			
			mAdvertisingIdFetcher.Fetch(false, adid =>
			{
				if (mDeviceIdFetcher == null)
					mDeviceIdFetcher = new DeviceIdFetcher();
				mDeviceIdFetcher.Fetch(false, dvid =>
				{
					var providers = new List<SnipeObject>();
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
					
					mCommunicator.CreateRequest(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)?.RequestUnauthorized(
						new SnipeObject()
						{
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

								StartBindings();

								//callback?.Invoke(true);
							}
							// else
							// {
								// callback?.Invoke(false);
							// }
						});
				});
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
			foreach (var binding in mBindings)
			{
				binding?.Start();
			}
		}

		public void ClaimRestoreToken(string token, Action<bool> callback)
		{
			if (mCommunicator == null)
			{
				callback?.Invoke(false);
				return;
			}

			mCommunicator.CreateRequest(SnipeMessageTypes.AUTH_RESTORE)?.RequestUnauthorized(
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
			if (mBindings == null)
			{
				result_binding = new BindingType();
				mBindings = new List<AuthBinding>()
				{
					result_binding,
				};
			}
			else
			{
				foreach (var binding in mBindings)
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
					foreach (var binding in mBindings)
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

		private void ClearAllBindings()
		{
			PlayerPrefs.DeleteKey(SnipePrefs.AUTH_BIND_DONE + "dvid");

			if (mBindings != null)
			{
				foreach (var binding in mBindings)
				{
					PlayerPrefs.DeleteKey(binding.BindDonePrefsKey);
				}
			}
		}

		// Called by BindProvider
		internal void InvokeAccountBindingCollisionEvent(AuthBinding binding, string user_name = null)
		{
			AccountBindingCollision?.Invoke(binding, user_name);
		}
	}
}