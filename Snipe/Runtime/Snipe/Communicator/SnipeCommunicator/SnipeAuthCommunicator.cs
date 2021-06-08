using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeAuthCommunicator
	{
		private const float LOGING_TOKEN_REFRESH_TIMEOUT = 1800.0f; // = 30 min

		public delegate void AccountRegisterResponseHandler(string error_code, int user_id = 0);
		public event AccountRegisterResponseHandler AccountRegisterResponse;

		public delegate void AccountBindingCollisionHandler(BindProvider provider, string user_name = null);
		public event AccountBindingCollisionHandler AccountBindingCollision;
		
		public delegate void GetUserAttributeCallback(string error_code, string user_name, string key, object value);

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
		public string LoginToken { get; private set; }

		private float mLoginTokenExpiry = -1;
		private Coroutine mCheckLoginTokenExpiryCoroutine;

		public bool JustRegistered { get; private set; } = false;

		private List<AuthProvider> mAuthProviders;
		private AuthProvider mCurrentProvider;

		private Action mAuthSucceededCallback;
		private Action mAuthFailedCallback;

		private bool mRebindAllProviders = false;

		public void ClearLoginToken()
		{
			LoginToken = "";
		}

		public ProviderType AddAuthProvider<ProviderType>() where ProviderType : AuthProvider, new()
		{
			ProviderType auth_provider = GetAuthProvider<ProviderType>();
			if (auth_provider == null)
			{
				auth_provider = new ProviderType();
				
				if (mAuthProviders == null)
					mAuthProviders = new List<AuthProvider>();
				
				mAuthProviders.Add(auth_provider);
			}

			return auth_provider;
		}

		public List<AuthProvider> GetAuthProviders()
		{
			return mAuthProviders;
		}

		public ProviderType GetAuthProvider<ProviderType>() where ProviderType : AuthProvider
		{
			if (mAuthProviders != null)
			{
				foreach (AuthProvider provider in mAuthProviders)
				{
					if (provider != null && provider is ProviderType)
					{
						return provider as ProviderType;
					}
				}
			}

			return null;
		}

		public AuthProvider GetAuthProvider(string provider_id)
		{
			if (mAuthProviders != null)
			{
				foreach (AuthProvider provider in mAuthProviders)
				{
					if (provider != null && provider.ProviderId == provider_id)
					{
						return provider;
					}
				}
			}

			return null;
		}

		public bool SetCurrentProvider(AuthProvider provider)
		{
			DebugLogger.Log($"[SnipeAuthCommunicator] SetCurrentProvider - {provider?.ProviderId}");

			if (provider == null)
			{
				if (mCurrentProvider != null)
				{
					mCurrentProvider.Dispose();
					mCurrentProvider = null;
				}
				return false;
			}

			if (mCurrentProvider == provider || mCurrentProvider?.ProviderId == provider?.ProviderId)
				return true;

			if (mAuthProviders != null)
			{
				if (mAuthProviders.IndexOf(provider) >= 0)
				{
					if (mCurrentProvider != null)
						mCurrentProvider.Dispose();

					mCurrentProvider = provider;
					return true;
				}
				else
				{
					var added_provider = GetAuthProvider(provider.ProviderId);
					if (added_provider != null)
					{
						if (mCurrentProvider != null)
							mCurrentProvider.Dispose();

						mCurrentProvider = added_provider;
						return true;
					}
				}
			}

			return false;
		}

		public void SwitchToDefaultProvider()
		{
			SwitchToDefaultAuthProvider();
		}

		public void BindAllProviders(bool force_all = false, BindProvider.BindResultCallback single_bind_callback = null)
		{
			if (mAuthProviders != null)
			{
				foreach (BindProvider provider in mAuthProviders)
				{
					if (provider != null && (force_all || provider.AccountExists == false))
					{
						provider.RequestBind(single_bind_callback);
					}
				}
			}
		}

		private void ClearAllBindings()
		{
			if (mAuthProviders != null)
			{
				foreach (BindProvider provider in mAuthProviders)
				{
					if (provider != null)
					{
						PlayerPrefs.DeleteKey(provider.BindDonePrefsKey);
					}
				}
			}
		}

		public void Authorize<ProviderType>(Action succeess_callback, Action fail_callback = null) where ProviderType : AuthProvider
		{
			mCurrentProvider = GetAuthProvider<ProviderType>();

			if (mCurrentProvider == null)
			{
				DebugLogger.Log("[SnipeAuthCommunicator] Authorize<ProviderType> - provider not found");

				if (fail_callback != null)
					fail_callback.Invoke();

				return;
			}

			AuthorizeWithCurrentProvider(succeess_callback, fail_callback);
		}

		public void Authorize(Action succeess_callback, Action fail_callback = null)
		{
			if (mCurrentProvider == null)
				SwitchToNextAuthProvider();

			AuthorizeWithCurrentProvider(succeess_callback, fail_callback);
		}

		public void Authorize(bool reset, Action succeess_callback, Action fail_callback = null)
		{
			if (reset) // forget previous provider and start again from the beginning
			{
				ClearLoginToken();

				AuthProvider prev_provider = mCurrentProvider;

				mCurrentProvider = null; 
				SwitchToNextAuthProvider();

				if (prev_provider != mCurrentProvider)
					prev_provider.Dispose();
			}

			Authorize(succeess_callback, fail_callback);
		}

		/// <summary>
		/// Clear all auth data an authorize using specified <c>AuthProvider</c>.
		/// </summary>
		public void ClearAuthDataAndSetCurrentProvider(AuthProvider provider)
		{
			PlayerPrefs.DeleteKey(SnipePrefs.LOGIN_USER_ID);
			
			foreach (BindProvider bind_provider in mAuthProviders)
			{
				if (bind_provider != null)
				{
					bind_provider.IsBindDone = false;
				}
			}
			
			ClearLoginToken();
			SetCurrentProvider(provider);
		}

		/// <summary>
		/// After successful authorization with current provider <c>BindAllProviders(true)</c> will be called
		/// </summary>
		public void RebindAllProvidersAfterAuthorization()
		{
			mRebindAllProviders = true;
		}

		public void ClaimRestoreToken(string token, Action<bool> callback)
		{
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_RESTORE)?.RequestAuth(
				new SnipeObject()
				{
					["token"] = token,
				},
				(error_code, response) =>
				{
					if (error_code == "ok")
					{
						ClearAllBindings();
						UserID = 0;
						LoginToken = "";
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
		
		public void GetUserAttribute(string provider_id, string user_id, string key, GetUserAttributeCallback callback)
		{
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_ATTR_GET)?.RequestAuth(
				new SnipeObject()
				{
					["provider"] = provider_id,
					["login"] = user_id,
					["key"] = key,
				},
				(error_code, response) =>
				{
					if (callback != null)
					{
						if (response != null)
						{
							callback.Invoke(error_code, response?.SafeGetString("name"), response?.SafeGetString("key"), response?["val"]);
						}
						else
						{
							callback.Invoke("error", "", key, null);
						}
					}
				});
		}

		internal void InvokeAccountBindingCollisionEvent(BindProvider provider, string user_name = null)
		{
			AccountBindingCollision?.Invoke(provider, user_name);
		}

		protected void AuthorizeWithCurrentProvider(Action succeess_callback, Action fail_callback = null)
		{
			JustRegistered = false;

			mAuthSucceededCallback = succeess_callback;
			mAuthFailedCallback = fail_callback;

			bool reset_auth = !(mCurrentProvider is DefaultAuthProvider) || string.IsNullOrEmpty(PlayerPrefs.GetString(SnipePrefs.AUTH_KEY));
			mCurrentProvider.RequestAuth(OnCurrentProviderAuthSuccess, OnCurrentProviderAuthFail, reset_auth);
		}

		private void SwitchToNextAuthProvider(bool create_default = true)
		{
			AuthProvider prev_provider = mCurrentProvider;
			mCurrentProvider = null;

			if (mAuthProviders != null && mAuthProviders.Count > 0)
			{
				int next_index = 0;
				if (prev_provider != null)
				{
					next_index = mAuthProviders.IndexOf(prev_provider) + 1;
					if (next_index < 0)
						next_index = 0;
				}

				if (mAuthProviders.Count > next_index)
				{
					mCurrentProvider = mAuthProviders[next_index];
				}
			}

			if (mCurrentProvider == null && create_default)
			{
				mCurrentProvider = new DefaultAuthProvider();
			}
		}

		private void SwitchToDefaultAuthProvider()
		{
			if (mCurrentProvider != null && !(mCurrentProvider is DefaultAuthProvider))
			{
				mCurrentProvider.Dispose();
				mCurrentProvider = null;
			}
			if (mCurrentProvider == null)
				mCurrentProvider = new DefaultAuthProvider();
		}

		private void OnCurrentProviderAuthSuccess(int user_id, string login_token)
		{
			UserID = user_id;
			LoginToken = login_token;

			InvokeAuthSuccessCallback();

			ResetCheckLoginTokenExpiryCoroutine();

			mCurrentProvider?.Dispose();
			mCurrentProvider = null;

			BindAllProviders(mRebindAllProviders);
			mRebindAllProviders = false;
		}

		private void OnCurrentProviderAuthFail(string error_code)
		{
			DebugLogger.Log("[SnipeAuthCommunicator] OnCurrentProviderAuthFail (" + (mCurrentProvider != null ? mCurrentProvider.ProviderId : "null") + ") error_code: " + error_code);

			mRebindAllProviders = false;

			if (mCurrentProvider is DefaultAuthProvider)
			{
				if (error_code == SnipeErrorCodes.NOT_INITIALIZED || error_code == SnipeErrorCodes.NO_SUCH_USER)
				{
					RequestRegister();
				}
				else
				{
					InvokeAuthFailCallback();
				}
			}
			else  // try next provider
			{
				if (mCurrentProvider != null)
					mCurrentProvider.Dispose();

				SwitchToNextAuthProvider();
				bool reset_auth = !(mCurrentProvider is DefaultAuthProvider) || string.IsNullOrEmpty(PlayerPrefs.GetString(SnipePrefs.AUTH_KEY));
				mCurrentProvider.RequestAuth(OnCurrentProviderAuthSuccess, OnCurrentProviderAuthFail, reset_auth);
			}
		}

		private void InvokeAuthSuccessCallback()
		{
			if (mAuthSucceededCallback != null)
				mAuthSucceededCallback.Invoke();

			mAuthSucceededCallback = null;
			mAuthFailedCallback = null;
		}

		private void InvokeAuthFailCallback()
		{
			if (mAuthFailedCallback != null)
				mAuthFailedCallback.Invoke();

			mAuthSucceededCallback = null;
			mAuthFailedCallback = null;

			mCurrentProvider?.Dispose();
			mCurrentProvider = null;
		}

		private void RequestRegister()
		{
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_REGISTER)?.RequestAuth(null,
				(error_code, response) =>
				{
					int user_id = 0;
					
					if (error_code == "ok")
					{
						JustRegistered = true;

						string auth_login = response.SafeGetString("uid");
						string auth_token = response.SafeGetString("password");

						PlayerPrefs.SetString(SnipePrefs.AUTH_UID, auth_login);
						PlayerPrefs.SetString(SnipePrefs.AUTH_KEY, auth_token);

						user_id = response.SafeGetValue<int>("id");
						
						Analytics.SetUserId(user_id.ToString());
						Analytics.TrackEvent(Analytics.EVENT_ACCOUNT_REGISTERED, new SnipeObject()
						{
							["user_id"] = user_id,
						});

						SwitchToDefaultAuthProvider();
						mCurrentProvider.RequestAuth(OnCurrentProviderAuthSuccess, OnCurrentProviderAuthFail);

						BindAllProviders(false);
					}
					else
					{
						Analytics.TrackEvent(Analytics.EVENT_ACCOUNT_REGISTERATION_FAILED, new SnipeObject()
						{
							["error_code"] = error_code,
						});
						
						InvokeAuthFailCallback();
					}

					AccountRegisterResponse?.Invoke(error_code, user_id);
				});
		}

		private void ResetCheckLoginTokenExpiryCoroutine()
		{
			if (mCheckLoginTokenExpiryCoroutine != null)
				SnipeCommunicator.Instance.StopCoroutine(mCheckLoginTokenExpiryCoroutine);

			mCheckLoginTokenExpiryCoroutine = SnipeCommunicator.Instance.StartCoroutine(CheckLoginTokenExpiryCoroutine());
		}

		private IEnumerator CheckLoginTokenExpiryCoroutine()
		{
			mLoginTokenExpiry = Time.realtimeSinceStartup + LOGING_TOKEN_REFRESH_TIMEOUT;
			while (mLoginTokenExpiry > Time.realtimeSinceStartup)
				yield return null;

			mCheckLoginTokenExpiryCoroutine = null;
			RefreshLoginToken();
		}

		private void RefreshLoginToken()
		{
			if (mAuthSucceededCallback != null)
				return;

			SwitchToDefaultAuthProvider();
			mCurrentProvider.RequestAuth(OnCurrentProviderAuthSuccess, OnCurrentProviderAuthFail);
		}
	}
}