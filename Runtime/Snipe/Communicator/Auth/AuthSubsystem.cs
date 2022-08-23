using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MiniIT;

namespace MiniIT.Snipe
{
	public class AuthSubsystem
	{
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
		
		private InternalAuthProvider mInternalAuthProvider;
		
		public AuthSubsystem(SnipeCommunicator communicator)
		{
			mCommunicator = communicator;
		}

		public void Authorize()//AuthResultCallback callback = null)
		{
			// if (mCurrentProvider == null)
			//{
				if (!string.IsNullOrEmpty(PlayerPrefs.GetString(SnipePrefs.AUTH_KEY)))
					LoginWithInternalAuthData();
				else
					RegisterAndLogin();
			//}

			//AuthorizeWithCurrentProvider(callback);
		}
		
		public void LoginWithInternalAuthData()
		{
			if (mInternalAuthProvider == null)
				mInternalAuthProvider = new InternalAuthProvider();
			mInternalAuthProvider.RequestAuth();
		}
		
		
		public void RegisterAndLogin()
		{
			if (mCommunicator == null)
			{
				//callback?.Invoke(false);
				return;
			}
			
			if (mAdvertisingIdFetcher == null)
				mAdvertisingIdFetcher = new AdvertisingIdFetcher();
			
			mAdvertisingIdFetcher.Fetch(adid =>
			{
				if (mDeviceIdFetcher == null)
					mDeviceIdFetcher = new DeviceIdFetcher();
				mDeviceIdFetcher.Fetch(dvid =>
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
					
					mCommunicator.CreateRequest(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN)?.RequestAuth(
						new SnipeObject()
						{
							["ckey"] = SnipeConfig.ClientKey,
							["auths"] = providers,
						},
						(error_code, response) =>
						{
							if (error_code == "ok")
							{
								//ClearAllBindings();
								UserID = response.SafeGetValue<int>("id");
								PlayerPrefs.SetString(SnipePrefs.AUTH_UID, response.SafeGetString("uid"));
								PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, response.SafeGetString("password"));
								PlayerPrefs.Save();
								
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
		


		// public void ClaimRestoreToken(string token, Action<bool> callback)
		// {
			// if (mCommunicator == null)
			// {
				// callback?.Invoke(false);
				// return;
			// }
			
			// mCommunicator.CreateRequest(SnipeMessageTypes.AUTH_RESTORE)?.RequestAuth(
				// new SnipeObject()
				// {
					// ["ckey"] = SnipeConfig.ClientKey,
					// ["token"] = token,
				// },
				// (error_code, response) =>
				// {
					// if (error_code == "ok")
					// {
						// ClearAllBindings();
						// UserID = 0;
						// PlayerPrefs.SetString(SnipePrefs.AUTH_UID, response.SafeGetString("uid"));
						// PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, response.SafeGetString("password"));
						// PlayerPrefs.Save();
						// callback?.Invoke(true);
					// }
					// else
					// {
						// callback?.Invoke(false);
					// }
				// });
		// }
		
		

	}
}