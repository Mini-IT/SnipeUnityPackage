using System;
using System.Collections.Generic;
using System.Linq;
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

		private readonly IRequestFactory _requestFactory;
		private Func<int> _getVersionDelegate;
		private readonly Dictionary<string, AbstractProfileAttribute> _attributes = new();
		private readonly Dictionary<string, Action<object>> _attributeValueSetters = new();
		private readonly Dictionary<string, Func<object>> _localValueGetters = new();
		private readonly List<Action> _serverEventUnsubscribers = new();
		private readonly ISharedPrefs _sharedPrefs;
		private readonly PlayerPrefsStringListHelper _stringListHelper;
		private readonly PlayerPrefsTypeHelper _prefsHelper;
		private bool _syncInProgress;
		private bool _disposed;

		public ProfileManager(IRequestFactory requestFactory, ISharedPrefs sharedPrefs)
		{
			_requestFactory = requestFactory;
			_sharedPrefs = sharedPrefs;

			_stringListHelper = new PlayerPrefsStringListHelper(_sharedPrefs);
			_prefsHelper = new PlayerPrefsTypeHelper(_sharedPrefs);
		}

		public void Initialize(SnipeApiReadOnlyUserAttribute<int> versionAttr)
		{
			if (_getVersionDelegate != null)
			{
				Dispose();
			}

			_getVersionDelegate = versionAttr.GetValue;

			SyncWithServer();
		}

		public ProfileAttribute<T> GetAttribute<T>(SnipeApiReadOnlyUserAttribute<T> serverAttribute)
		{
			string key = serverAttribute.Key;
			if (_attributes.TryGetValue(key, out var attr))
			{
				return (ProfileAttribute<T>)attr;
			}

			var newAttr = new ProfileAttribute<T>(key, this, _sharedPrefs);
			_attributes[key] = newAttr;
			_attributeValueSetters[key] = (val) => newAttr.SetValueFromServer((T)val);
			_localValueGetters[key] = () => GetLocalValue<T>(key);

			// Subscribe to server attribute ValueChanged event
			SubscribeToServerAttribute(serverAttribute);

			// Initialize value from server or local storage
			InitializeAttributeValue(serverAttribute, newAttr);

			return newAttr;
		}

		public LocalProfileAttribute<T> GetLocalAttribute<T>(string key)
		{
			return GetLocalAttribute(key, () => new LocalProfileAttribute<T>(key, _sharedPrefs));
		}

		public LocalProfileAttribute<T> GetLocalAttribute<T>(string key, T defaultValue)
		{
			return GetLocalAttribute(key, () => new LocalProfileAttribute<T>(key, _sharedPrefs, defaultValue));
		}

		private LocalProfileAttribute<T> GetLocalAttribute<T>(string key, Func<LocalProfileAttribute<T>> factory)
		{
			if (_attributes.TryGetValue(key, out var attr))
			{
				return (LocalProfileAttribute<T>)attr;
			}

			var newAttr = factory.Invoke();
			_attributes[key] = newAttr;

			return newAttr;
		}

		private void InitializeAttributeValue<T>(SnipeApiReadOnlyUserAttribute<T> serverAttr, ProfileAttribute<T> attr)
		{
			var key = serverAttr.Key;
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

		private void SubscribeToServerAttribute<T>(SnipeApiReadOnlyUserAttribute<T> serverAttr)
		{
			if (serverAttr == null)
			{
				return;
			}

			var key = serverAttr.Key;
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
			int localVersion = GetLocalVersion() + 1;
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

			// If we have local unsynced changes and server version is older than local version,
			// preserve local changes and don't overwrite with server value.
			// This prevents losing offline progress when reconnecting.
			// Note: If serverVersion == 0 (uninitialized) and we have local changes, we preserve local changes
			// because they represent newer offline progress. If serverVersion >= localVersion, server is authoritative.
			bool shouldPreserveLocalChanges = hasLocalChange && serverVersion < localVersion;

			if (!shouldPreserveLocalChanges)
			{
				// Server value is authoritative - accept it
				// Update local storage with server value
				SetLocalValue(key, value);

				// Update attribute value
				if (_attributeValueSetters.TryGetValue(key, out var setter))
				{
					setter(value);
				}
			}
			else
			{
				// We have local changes that are newer - keep them and ensure they stay in dirty keys
				// The local value is already correct, so we don't need to update it
				// Dirty keys will remain so the local value can be synced to server
			}

			// Remove from dirty set if server version is >= local version
			// This means the server has the latest version of this attribute
			// If serverVersion < localVersion, we preserve local changes so dirty keys remain
			if (hasLocalChange)
			{
				if (serverVersion >= localVersion)
				{
					_stringListHelper.Remove(KEY_DIRTY_KEYS, key);
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

			var serverVersion = _getVersionDelegate.Invoke();
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
				if (_localValueGetters.TryGetValue(key, out var getter))
				{
					var value = getter();
					if (value != null)
					{
						pendingChanges[key] = value;
					}
				}
			}

			return pendingChanges;
		}

		private void SendPendingChanges()
		{
			if (_syncInProgress || _requestFactory == null)
			{
				return;
			}

			var pendingChanges = RebuildPendingChanges();
			if (pendingChanges.Count == 0)
			{
				return;
			}

			_syncInProgress = true;

			AbstractCommunicatorRequest request;

			if (pendingChanges.Count == 1)
			{
				var item = pendingChanges.First();

				request = _requestFactory.CreateRequest("attr.set", new Dictionary<string, object>()
				{
					["key"] = item.Key,
					["val"] = item.Value
				});
			}
			else
			{
				var setMultiData = new List<IDictionary<string, object>>();
				foreach (var kvp in pendingChanges)
				{
					var data = ToSetMultiMap(kvp.Key, kvp.Value, "set");
					setMultiData.Add(data);
				}

				request = _requestFactory.CreateRequest("attr.setMulti", new Dictionary<string, object>()
				{
					["data"] = setMultiData
				});
			}

			if (request != null)
			{
				request.Request((errorCode, _) =>
				{
					_syncInProgress = false;

					if (errorCode == "ok")
					{
						// Success - clear dirty keys and update versions
						_stringListHelper.Clear(KEY_DIRTY_KEYS);
						if (_getVersionDelegate != null)
						{
							var serverVersion = _getVersionDelegate.Invoke();
							SetLocalVersion(serverVersion);
							SetLastSyncedVersion(serverVersion);
						}
					}
					else
					{
						// Failure - keep dirty keys for retry
						Debug.LogWarning($"ProfileManager: attr.set/setMulti failed with error {errorCode}");
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

		internal T GetLocalValue<T>(string key)
		{
			var prefsKey = KEY_ATTR_PREFIX + key;
			return GetPrefsValue<T>(prefsKey);
		}

		internal T GetPrefsValue<T>(string prefsKey)
		{
			return _prefsHelper.GetPrefsValue<T>(prefsKey);
		}

		internal void SetLocalValue(string key, object value)
		{
			var prefsKey = KEY_ATTR_PREFIX + key;
			_prefsHelper.SetLocalValue(prefsKey, value);
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
			_attributeValueSetters.Clear();
			_localValueGetters.Clear();

			_getVersionDelegate = null;
		}
	}
}
