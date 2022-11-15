using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MiniIT;

namespace MiniIT.Snipe
{
	using AuthResultCallback = AuthProvider.AuthResultCallback;
	using AccountRegisterResponseHandler = AuthProvider.AuthResultCallback;

	public class SnipeAuthCommunicator
	{
		public delegate void AccountBindingCollisionHandler(BindProvider provider, string user_name = null);

		public event AccountRegisterResponseHandler AccountRegisterResponse;
		public event AccountBindingCollisionHandler AccountBindingCollision;
		
		public delegate void GetUserAttributeCallback(string error_code, string user_name, string key, object value);

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

		private static List<AuthProvider> _authProviders;
		private static AuthProvider _currentProvider;

		private AuthResultCallback _authResultCallback;

		private static bool _rebindAllProviders = false;

		public ProviderType AddAuthProvider<ProviderType>() where ProviderType : AuthProvider, new()
		{
			ProviderType auth_provider = GetAuthProvider<ProviderType>();
			if (auth_provider == null)
			{
				auth_provider = new ProviderType();
				
				if (_authProviders == null)
					_authProviders = new List<AuthProvider>();
				
				_authProviders.Add(auth_provider);
			}

			return auth_provider;
		}

		public List<AuthProvider> GetAuthProviders()
		{
			return _authProviders;
		}

		public ProviderType GetAuthProvider<ProviderType>() where ProviderType : AuthProvider
		{
			if (_authProviders != null)
			{
				foreach (AuthProvider provider in _authProviders)
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
			if (_authProviders != null)
			{
				foreach (AuthProvider provider in _authProviders)
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
				if (_currentProvider != null)
				{
					_currentProvider.DisposeCallbacks();
					_currentProvider = null;
				}
				return false;
			}

			if (_currentProvider == provider || _currentProvider?.ProviderId == provider?.ProviderId)
				return true;

			if (_authProviders != null)
			{
				if (_authProviders.IndexOf(provider) >= 0)
				{
					if (_currentProvider != null)
						_currentProvider.DisposeCallbacks();

					_currentProvider = provider;
					return true;
				}
				else
				{
					var added_provider = GetAuthProvider(provider.ProviderId);
					if (added_provider != null)
					{
						if (_currentProvider != null)
							_currentProvider.DisposeCallbacks();

						_currentProvider = added_provider;
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
			if (_authProviders != null)
			{
				foreach (var auth_provider in _authProviders)
				{
					if (auth_provider is BindProvider provider && (force_all || provider.AccountExists == false))
					{
						provider.RequestBind(single_bind_callback);
					}
				}
			}
		}

		private void ClearAllBindings()
		{
			if (_authProviders != null)
			{
				foreach (var auth_provider in _authProviders)
				{
					if (auth_provider is BindProvider provider)
					{
						PlayerPrefs.DeleteKey(provider.BindDonePrefsKey);
					}
				}
			}
		}

		public void Authorize<ProviderType>(AuthResultCallback callback = null) where ProviderType : AuthProvider
		{
			_currentProvider = GetAuthProvider<ProviderType>();

			if (_currentProvider == null)
			{
				DebugLogger.Log("[SnipeAuthCommunicator] Authorize<ProviderType> - provider not found");

				callback?.Invoke(SnipeErrorCodes.NOT_INITIALIZED, 0);

				return;
			}

			AuthorizeWithCurrentProvider(callback);
		}

		public void Authorize(AuthResultCallback callback = null)
		{
			if (_currentProvider == null)
			{
				if (!string.IsNullOrEmpty(PlayerPrefs.GetString(SnipePrefs.AUTH_KEY)))
					SwitchToDefaultProvider();
				else
					SwitchToNextAuthProvider();
			}

			AuthorizeWithCurrentProvider(callback);
		}

		public void Authorize(bool reset, AuthResultCallback callback = null)
		{
			if (reset) // forget previous provider and start again from the beginning
			{
				AuthProvider prev_provider = _currentProvider;

				_currentProvider = null; 
				SwitchToNextAuthProvider();

				if (prev_provider != _currentProvider)
					prev_provider.DisposeCallbacks();
			}

			Authorize(callback);
		}

		/// <summary>
		/// Clear all auth data and authorize using specified <c>AuthProvider</c>.
		/// </summary>
		public void ClearAuthDataAndSetCurrentProvider(AuthProvider provider)
		{
			PlayerPrefs.DeleteKey(SnipePrefs.LOGIN_USER_ID);
			PlayerPrefs.DeleteKey(SnipePrefs.AUTH_UID);
			PlayerPrefs.DeleteKey(SnipePrefs.AUTH_KEY);
			
			foreach (var auth_provider in _authProviders)
			{
				if (auth_provider is BindProvider bind_provider)
				{
					bind_provider.IsBindDone = false;
				}
			}
			
			SetCurrentProvider(provider);
		}

		/// <summary>
		/// After successful authorization with current provider <c>BindAllProviders(true)</c> will be called
		/// </summary>
		public void RebindAllProvidersAfterAuthorization()
		{
			_rebindAllProviders = true;
		}

		public void ClaimRestoreToken(string token, Action<bool> callback)
		{
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_RESTORE)?.RequestAuth(
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

		private void AuthorizeWithCurrentProvider(AuthResultCallback callback = null)
		{
			JustRegistered = false;
			_authResultCallback = callback;
			CurrentProviderRequestAuth();
		}

		private void CurrentProviderRequestAuth()
		{
			bool reset_auth = !(_currentProvider is DefaultAuthProvider) || string.IsNullOrEmpty(PlayerPrefs.GetString(SnipePrefs.AUTH_KEY));
			_currentProvider.RequestAuth(OnCurrentProviderAuthResult, reset_auth);
		}

		private void SwitchToNextAuthProvider(bool create_default = true)
		{
			AuthProvider prev_provider = _currentProvider;
			_currentProvider = null;

			if (_authProviders != null && _authProviders.Count > 0)
			{
				int next_index = 0;
				if (prev_provider != null)
				{
					next_index = _authProviders.IndexOf(prev_provider) + 1;
				}

				if (_authProviders.Count > next_index)
				{
					_currentProvider = _authProviders[next_index];
				}
			}

			if (_currentProvider == null && create_default)
			{
				_currentProvider = new DefaultAuthProvider();
			}
		}

		private void SwitchToDefaultAuthProvider()
		{
			if (_currentProvider != null && !(_currentProvider is DefaultAuthProvider))
			{
				_currentProvider.DisposeCallbacks();
				_currentProvider = null;
			}
			if (_currentProvider == null)
				_currentProvider = new DefaultAuthProvider();
		}

		private void OnCurrentProviderAuthResult(string error_code, int user_id = 0)
		{
			if (user_id != 0)
			{
				UserID = user_id;

				InvokeAuthSuccessCallback(user_id);

				_currentProvider?.DisposeCallbacks();
				_currentProvider = null;

				BindAllProviders(_rebindAllProviders);
				_rebindAllProviders = false;
			}
			else
			{
				DebugLogger.Log("[SnipeAuthCommunicator] OnCurrentProviderAuthFail (" + (_currentProvider != null ? _currentProvider.ProviderId : "null") + ") error_code: " + error_code);
				
				_rebindAllProviders = false;
				
				if (error_code == SnipeErrorCodes.NO_SUCH_USER ||
					error_code == SnipeErrorCodes.NO_SUCH_AUTH ||
					error_code == SnipeErrorCodes.NOT_INITIALIZED)
				{
					if (_authProviders != null && _authProviders.Count > _authProviders.IndexOf(_currentProvider) + 1)
					{
						// try next provider
						_currentProvider?.DisposeCallbacks();

						SwitchToNextAuthProvider();
						CurrentProviderRequestAuth();
					}
					else // all providers failed
					{
						RequestRegister();
					}
				}
				else
				{
					InvokeAuthFailCallback(error_code);
				}
			}
		}

		private void InvokeAuthSuccessCallback(int user_id)
		{
			_authResultCallback?.Invoke(SnipeErrorCodes.OK, user_id);
			_authResultCallback = null;
		}

		private void InvokeAuthFailCallback(string error_code)
		{
			_authResultCallback?.Invoke(error_code, 0);
			_authResultCallback = null;

			_currentProvider?.DisposeCallbacks();
			_currentProvider = null;
		}

		private void RequestRegister()
		{
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			
			SnipeCommunicator.Instance.CreateRequest(SnipeMessageTypes.AUTH_USER_REGISTER)?.RequestAuth(null,
				(error_code, response) =>
				{
					stopwatch.Stop();
					
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
							["request_time"] = stopwatch.ElapsedMilliseconds,
						});

						SwitchToDefaultAuthProvider();
						_currentProvider.RequestAuth(OnCurrentProviderAuthResult);

						BindAllProviders(false);
					}
					else
					{
						Analytics.TrackEvent(Analytics.EVENT_ACCOUNT_REGISTERATION_FAILED, new SnipeObject()
						{
							["error_code"] = error_code,
							["request_time"] = stopwatch.ElapsedMilliseconds,
						});
						
						InvokeAuthFailCallback(error_code);
					}

					AccountRegisterResponse?.Invoke(error_code, user_id);
				});
		}
	}
}