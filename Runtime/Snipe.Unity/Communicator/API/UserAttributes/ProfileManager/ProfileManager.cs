using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MiniIT.Snipe;
using MiniIT.Storage;
using UnityEngine;

namespace MiniIT.Snipe.Api
{
	public class ProfileManager : IDisposable
	{
		internal const string KEY_LOCAL_VERSION = "profile_local_version";
		internal const string KEY_LAST_SYNCED_VERSION = "profile_last_synced_version";
		internal const string KEY_DIRTY_KEYS = "profile_dirty_keys";
		internal const string KEY_ATTR_PREFIX = "profile_attr_";

		private AbstractSnipeApiService _snipeApiService;
		private SnipeApiUserAttributes _userAttributes;
		private Func<int> _getVersionDelegate;
		private readonly Dictionary<string, IProfileAttribute> _attributes = new ();
		private readonly Dictionary<string, Type> _attributeTypes = new ();
		private readonly List<Action> _serverEventUnsubscribers = new ();
		private ISharedPrefs _sharedPrefs;
		private PlayerPrefsStringListHelper _stringListHelper;
		private bool _syncInProgress;
		private bool _disposed;

		public void Initialize(AbstractSnipeApiService snipeApiService, SnipeApiUserAttributes userAttributes, ISharedPrefs sharedPrefs)
		{
			if (_snipeApiService != null)
			{
				Dispose();
			}

			_snipeApiService = snipeApiService;
			_userAttributes = userAttributes;
			_sharedPrefs = sharedPrefs;

			_stringListHelper = new PlayerPrefsStringListHelper(_sharedPrefs);

			// Get version delegate - try to get _version attribute from UserAttributes
			_getVersionDelegate = () =>
			{
				if (userAttributes.TryGetAttribute<int>("_version", out var versionAttr))
				{
					return versionAttr.GetValue();
				}
				return 0;
			};

			SyncWithServer();
		}

		public ProfileAttribute<T> GetAttribute<T>(SnipeApiUserAttribute attribute)
			=> GetAttribute<T>(attribute.Key);

		public ProfileAttribute<T> GetAttribute<T>(string key)
		{
			if (_attributes.TryGetValue(key, out var attr))
			{
				return (ProfileAttribute<T>)attr;
			}

			var newAttr = new ProfileAttribute<T>(key, this, _sharedPrefs);
			_attributes[key] = newAttr;
			_attributeTypes[key] = typeof(T);

			// Subscribe to server attribute ValueChanged event
			SubscribeToServerAttribute<T>(key);

			// Initialize value from server or local storage
			InitializeAttributeValue(key, newAttr);

			return newAttr;
		}

		private void InitializeAttributeValue<T>(string key, ProfileAttribute<T> attr)
		{
			// Try to get from server attribute first, but only if it's initialized
			if (_userAttributes.TryGetAttribute<T>(key, out var serverAttr))
			{
				if (serverAttr.IsInitialized)
				{
					// Server attribute is initialized - use its value
					attr.SetValueFromServer(serverAttr.GetValue());
					// Also update local storage to match server
					SetLocalValue(key, serverAttr.GetValue());
				}
				else
				{
					// Server attribute exists but not initialized yet - use local storage
					// The server value will arrive later via ValueChanged event
					var localValue = GetLocalValue<T>(key);
					attr.SetValueFromServer(localValue);
				}
			}
			else
			{
				// Server attribute doesn't exist - use local storage
				var localValue = GetLocalValue<T>(key);
				attr.SetValueFromServer(localValue);
			}
		}

		private void SubscribeToServerAttribute<T>(string key)
		{
			// Get the server attribute
			if (!_userAttributes.TryGetAttribute<T>(key, out var serverAttr))
			{
				return;
			}

			// Subscribe to ValueChanged event
			SnipeApiReadOnlyUserAttribute<T>.ValueChangedHandler handler = (oldValue, newValue) => OnServerAttributeChanged(key, newValue);
			serverAttr.ValueChanged += handler;

			// Store unsubscriber for cleanup
			_serverEventUnsubscribers.Add(() => serverAttr.ValueChanged -= handler);
		}

		internal void OnLocalAttributeChanged(string key, object value)
		{
			if (_disposed)
			{
				return;
			}

			// Save to local storage
			SetLocalValue(key, value);

			// Increment local version
			var localVersion = GetLocalVersion();
			localVersion++;
			SetLocalVersion(localVersion);

			// Add to dirty set (always, even if sync is in progress)
			_stringListHelper.Add(KEY_DIRTY_KEYS, key);

			// Try to send pending changes (only if not already in progress)
			if (!_syncInProgress)
			{
				SendPendingChanges();
			}
		}

		private void OnServerAttributeChanged(string key, object value)
		{
			if (_disposed)
			{
				return;
			}

			// Check if we have a local change that hasn't been synced yet
			bool hasLocalChange = _stringListHelper.Contains(KEY_DIRTY_KEYS, key);
			var localVersion = GetLocalVersion();
			var serverVersion = _getVersionDelegate?.Invoke() ?? 0;

			// Server value is authoritative - always accept it
			// Update local storage with server value
			SetLocalValue(key, value);

			// Remove from dirty set if server version is >= local version
			// This means the server has the latest version of this attribute
			// If serverVersion is 0 (uninitialized), we still accept the server value but keep local changes
			if (hasLocalChange)
			{
				if (serverVersion > 0 && serverVersion >= localVersion)
				{
					_stringListHelper.Remove(KEY_DIRTY_KEYS, key);
				}
			}

			// Update attribute value
			if (_attributes.TryGetValue(key, out var attr) && _attributeTypes.TryGetValue(key, out var attrType))
			{
				var method = typeof(ProfileAttribute<>).MakeGenericType(attrType).GetMethod(nameof(ProfileAttribute<object>.SetValueFromServer));
				if (method != null)
				{
					method.Invoke(attr, new[] { value });
				}
			}

			// Update version if server is newer
			if (serverVersion > localVersion)
			{
				SetLocalVersion(serverVersion);
			}
		}

		private void SyncWithServer()
		{
			if (_syncInProgress || _getVersionDelegate == null)
			{
				return;
			}

			var serverVersion = _getVersionDelegate();
			var localVersion = GetLocalVersion();
			var lastSyncedVersion = GetLastSyncedVersion();

			// Rebuild pending changes from dirty keys
			var pendingChanges = RebuildPendingChanges();

			if (localVersion > lastSyncedVersion)
			{
				// Client has unsynced changes
				SendPendingChanges();
			}
			else if (serverVersion > localVersion)
			{
				// Server has newer changes - accept all server values
				// This should already be handled by ValueChanged events, but clear dirty keys just in case
				_stringListHelper.Clear(KEY_DIRTY_KEYS);
				SetLocalVersion(serverVersion);
				SetLastSyncedVersion(serverVersion);
			}
			else
			{
				// Versions are equal - no action needed
				if (pendingChanges.Count == 0)
				{
					SetLastSyncedVersion(serverVersion);
				}
			}
		}

		private Dictionary<string, object> RebuildPendingChanges()
		{
			var pendingChanges = new Dictionary<string, object>();
			var dirtyKeys = _stringListHelper.GetList(KEY_DIRTY_KEYS);

			foreach (var key in dirtyKeys)
			{
				if (!_attributeTypes.TryGetValue(key, out var attrType))
				{
					continue;
				}

				object value = null;
				if (attrType == typeof(int))
				{
					value = GetLocalValue<int>(key);
				}
				else if (attrType == typeof(float))
				{
					value = GetLocalValue<float>(key);
				}
				else if (attrType == typeof(bool))
				{
					value = GetLocalValue<bool>(key);
				}
				else if (attrType == typeof(string))
				{
					value = GetLocalValue<string>(key);
				}

				if (value != null)
				{
					pendingChanges[key] = value;
				}
			}

			return pendingChanges;
		}

		private void SendPendingChanges()
		{
			if (_syncInProgress || _snipeApiService == null)
			{
				return;
			}

			var pendingChanges = RebuildPendingChanges();
			if (pendingChanges.Count == 0)
			{
				return;
			}

			_syncInProgress = true;

			var setMultiData = new List<IDictionary<string, object>>();
			foreach (var kvp in pendingChanges)
			{
				var data = ToSetMultiMap(kvp.Key, kvp.Value, "set");
				setMultiData.Add(data);
			}

			var request = _snipeApiService.CreateRequest("attr.setMulti", new Dictionary<string, object>()
			{
				["data"] = setMultiData
			});

			if (request != null)
			{
				request.Request((errorCode, responseData) =>
				{
					_syncInProgress = false;

					if (errorCode == "ok")
					{
						// Success - clear dirty keys and update versions
						_stringListHelper.Clear(KEY_DIRTY_KEYS);
						if (_getVersionDelegate != null)
						{
							var serverVersion = _getVersionDelegate();
							SetLocalVersion(serverVersion);
							SetLastSyncedVersion(serverVersion);
						}
					}
					else
					{
						// Failure - keep dirty keys for retry
						Debug.LogWarning($"ProfileManager: setMulti failed with error {errorCode}");
					}
				});
			}
			else
			{
				_syncInProgress = false;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static IDictionary<string, object> ToSetMultiMap(string key, object val, string action = "set")
		{
			return new Dictionary<string, object>()
			{
				["key"] = key ?? "",
				["val"] = val,
				["action"] = action ?? "set",
			};
		}

		private int GetLocalVersion()
		{
			return _sharedPrefs.GetInt(KEY_LOCAL_VERSION, 0);
		}

		private void SetLocalVersion(int version)
		{
			_sharedPrefs.SetInt(KEY_LOCAL_VERSION, version);
		}

		private int GetLastSyncedVersion()
		{
			return _sharedPrefs.GetInt(KEY_LAST_SYNCED_VERSION, 0);
		}

		private void SetLastSyncedVersion(int version)
		{
			_sharedPrefs.SetInt(KEY_LAST_SYNCED_VERSION, version);
		}

		private T GetLocalValue<T>(string key)
		{
			var prefsKey = KEY_ATTR_PREFIX + key;
			return GetPrefsValue<T>(prefsKey);
		}

		internal T GetPrefsValue<T>(string prefsKey)
		{
			if (typeof(T) == typeof(int))
			{
				return (T)(object)_sharedPrefs.GetInt(prefsKey, 0);
			}
			else if (typeof(T) == typeof(float))
			{
				return (T)(object)_sharedPrefs.GetFloat(prefsKey, 0f);
			}
			else if (typeof(T) == typeof(bool))
			{
				return (T)(object)(_sharedPrefs.GetInt(prefsKey, 0) == 1);
			}
			else if (typeof(T) == typeof(string))
			{
				return (T)(object)_sharedPrefs.GetString(prefsKey, "");
			}

			return default(T);
		}

		internal void SetLocalValue(string key, object value)
		{
			var prefsKey = KEY_ATTR_PREFIX + key;
			if (value is int intValue)
			{
				_sharedPrefs.SetInt(prefsKey, intValue);
			}
			else if (value is float floatValue)
			{
				_sharedPrefs.SetFloat(prefsKey, floatValue);
			}
			else if (value is bool boolValue)
			{
				_sharedPrefs.SetInt(prefsKey, boolValue ? 1 : 0);
			}
			else if (value is string stringValue)
			{
				_sharedPrefs.SetString(prefsKey, stringValue);
			}
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_syncInProgress = false;

			// Unsubscribe from server attributes
			foreach (var unsubscriber in _serverEventUnsubscribers)
			{
				try
				{
					unsubscriber?.Invoke();
				}
				catch (Exception e)
				{
					// ignore
				}
			}
			_serverEventUnsubscribers.Clear();

			foreach (var attr in _attributes.Values)
			{
				attr.Dispose();
			}
			_attributes.Clear();
			_attributeTypes.Clear();

			_snipeApiService = null;
			_userAttributes = null;
			_getVersionDelegate = null;
			_sharedPrefs = null;
			_stringListHelper = null;
		}
	}
}

