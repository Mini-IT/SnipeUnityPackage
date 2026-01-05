using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MiniIT;
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
		private readonly SnipeCommunicator _communicator;
		private readonly AuthSubsystem _auth;
		private readonly Dictionary<string, AbstractProfileAttribute> _attributes = new();
		private readonly Dictionary<string, Action<object>> _attributeValueSetters = new();
		private readonly Dictionary<string, Func<object>> _localValueGetters = new();
		private readonly ISharedPrefs _sharedPrefs;
		private readonly PlayerPrefsStringListHelper _stringListHelper;
		private readonly PlayerPrefsTypeHelper _prefsHelper;
		private bool _syncInProgress;
		private bool _disposed;
		private int _serverVersion;
		private string _serverVersionAttrKey;

		public ProfileManager(SnipeApiContext snipeContext, ISharedPrefs sharedPrefs)
			: this(snipeContext?.GetSnipeApiService(), snipeContext?.Communicator, snipeContext?.Auth, sharedPrefs)
		{
		}

		/// <summary>
		/// Internal constructor for tests and low-level integration scenarios.
		/// </summary>
		internal ProfileManager(IRequestFactory requestFactory, SnipeCommunicator communicator, AuthSubsystem auth, ISharedPrefs sharedPrefs)
		{
			_requestFactory = requestFactory;
			_communicator = communicator;
			_auth = auth;
			_sharedPrefs = sharedPrefs;

			_stringListHelper = new PlayerPrefsStringListHelper(_sharedPrefs, KEY_DIRTY_KEYS);
			_prefsHelper = new PlayerPrefsTypeHelper(_sharedPrefs);
		}

		public void Initialize(SnipeApiReadOnlyUserAttribute<int> versionAttr)
		{
			if (_serverVersionAttrKey != null)
			{
				Dispose();
			}
			_disposed = false;

			_serverVersionAttrKey = versionAttr.Key;
			_serverVersion = versionAttr.IsInitialized ? versionAttr.GetValue() : GetLastSyncedVersion();

			_communicator.MessageReceived += OnMessageReceived;

			SyncWithServer();
		}

		private void OnMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
		{
			HandleServerMessage(messageType, errorCode, data, requestId);
		}

		/// <summary>
		/// Internal entry point for tests. Production path goes through <see cref="OnMessageReceived"/>.
		/// </summary>
		internal void HandleServerMessage(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
		{
			if (_disposed)
			{
				return;
			}

			if (!string.Equals(errorCode, SnipeErrorCodes.OK, StringComparison.Ordinal))
			{
				return;
			}

			if (data == null || string.IsNullOrEmpty(messageType))
			{
				return;
			}

			switch (messageType)
			{
				// Lists of changed attributes
				case "attr.getAll":
					ApplyAttributeList(data, "data");
					break;
				case "attr.changed":
					ApplyAttributeList(data, "list");
					break;
				case "attr.getMulti":
				case "attr.getPrivate":
				case "attr.getPublic":
				case "attr.setMulti":
					if (IsSelfMessage(data))
					{
						ApplyAttributeList(data, "data");
					}
					break;

				// Single attribute value responses
				case "attr.get":
				case "attr.set":
				case "attr.inc":
				case "attr.dec":
					if (IsSelfMessage(data))
					{
						string key = data.SafeGetString("key");
						if (!string.IsNullOrEmpty(key) && data.TryGetValue("val", out object val))
						{
							ApplyServerAttributeChange(key, val);
						}
					}
					break;
			}
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
			_attributeValueSetters[key] = (val) => newAttr.SetValueFromServer(TypeConverter.Convert<T>(val));
			_localValueGetters[key] = () => GetLocalValue<T>(key);

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
			string key = serverAttr.Key;
			int localVersion = GetLocalVersion();

			if (_serverVersion >= localVersion)
			{
				// Server has newer version
				var serverValue = serverAttr.GetValue();
				attr.SetValueFromServer(serverValue);
				SetLocalValue(key, serverValue);

				_stringListHelper.Remove(key);
			}
			else // use local value
			{
				// Use local storage value (either server not initialized yet, or local changes are newer)
				var localValue = GetLocalValue<T>(key);
				attr.SetValueFromServer(localValue);
			}
		}

		internal void OnAttributeLocalValueChanged(string key, object value)
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
			_stringListHelper.Add(key);

			// Try to send local changes to server
			SyncWithServer();
		}

		private void ApplyServerAttributeChange(string key, object value)
		{
			if (_disposed)
			{
				return;
			}

			// Unknown attribute - ignore
			if (!_attributeValueSetters.TryGetValue(key, out Action<object> attrValueSetter))
			{
				return;
			}

			// Check if we have a local change that hasn't been synced yet

			var localVersion = GetLocalVersion();
			var lastSyncedVersion = GetLastSyncedVersion();

			// Ignore stale server pushes (older than last synced snapshot)
			if (_serverVersion <= lastSyncedVersion)
			{
				return;
			}

			// If we have local unsynced changes and server version is older than local version,
			// preserve local changes and don't overwrite with server value.
			// This prevents losing offline progress when reconnecting.
			// Note: If serverVersion == 0 (uninitialized) and we have local changes, we preserve local changes
			// because they represent newer offline progress. If serverVersion >= localVersion, server is authoritative.

			if (_serverVersion >= localVersion)
			{
				// Server value is authoritative - accept it
				// Update local storage with server value
				SetLocalValue(key, value);

				// Update attribute value
				attrValueSetter.Invoke(value);

				// Remove from dirty set if server version is >= local version
				// This means the server has the latest version of this attribute
				// If serverVersion < localVersion, we preserve local changes so dirty keys remain
				_stringListHelper.Remove(key);
			}
			else
			{
				// We might have local changes that are newer - keep them and ensure they stay in dirty keys
				// The local value is already correct, so we don't need to update it
				// Dirty keys will remain so the local value can be synced to server
			}

			// Update version if server is newer
			if (_serverVersion > localVersion)
			{
				SetLocalVersion(_serverVersion);
			}
		}

		private void ApplyAttributeList(IDictionary<string, object> data, string listKey)
		{
			if (!data.TryGetValue(listKey, out object rawList))
			{
				return;
			}

			if (rawList is not System.Collections.IList list)
			{
				return;
			}

			bool versionUpdated = false;

			// Udpate server version first
			if (_serverVersionAttrKey != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i] is IDictionary<string, object> item)
					{
						string key = item.SafeGetString("key");
						if (string.Equals(key, _serverVersionAttrKey, StringComparison.Ordinal))
						{
							if (item.TryGetValue("val", out object val))
							{
								int newServerVersion = TypeConverter.Convert<int>(val);
								_serverVersion = newServerVersion;
								versionUpdated = true;
							}
							break;
						}
					}
				}
			}

			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] is IDictionary<string, object> item)
				{
					string key = item.SafeGetString("key");

					if (_serverVersionAttrKey != null && string.Equals(key, _serverVersionAttrKey, StringComparison.Ordinal))
					{
						continue;
					}

					if (!string.IsNullOrEmpty(key) && item.TryGetValue("val", out object val))
					{
						ApplyServerAttributeChange(key, val);
					}
				}
			}

			if (versionUpdated)
			{
				SyncWithServer();
			}
		}

		private bool IsSelfMessage(IDictionary<string, object> data)
		{
			// A message is considered "self" when it has no explicit targeting info,
			// or it explicitly targets the currently logged-in user.

			// Explicit user id targeting (common for attr.get/getMulti/getPublic)
			if (data.TryGetValue("userID", out object userIdObj))
			{
				int targetUserId = TypeConverter.Convert<int>(userIdObj);
				int currentUserId = _auth?.UserID ?? 0;

				// If we don't know current user, we can't safely treat targeted messages as "self".
				if (currentUserId == 0)
				{
					return false;
				}

				return targetUserId == currentUserId;
			}

			// If server echoes login/provider, treat it as explicitly targeted (not clearly "self").
			string login = data.SafeGetString("login");
			string provider = data.SafeGetString("provider");
			if (!string.IsNullOrEmpty(login) || !string.IsNullOrEmpty(provider))
			{
				return false;
			}

			return true;
		}

		private void SyncWithServer()
		{
			if (_syncInProgress || _serverVersion < 1)
			{
				return;
			}

			int localVersion = GetLocalVersion();
			int lastSyncedVersion = GetLastSyncedVersion();

			// Rebuild pending changes from dirty keys
			var pendingChanges = RebuildPendingChanges();

			if (localVersion > lastSyncedVersion)
			{
				// Client has unsynced changes
				SendPendingChanges(pendingChanges);
			}
			else if (_serverVersion > localVersion)
			{
				// Server has newer changes - accept all server values
				// Values should be handled by incoming messages; clear dirty keys just in case.
				_stringListHelper.Clear();
				SetLocalVersion(_serverVersion);
				SetLastSyncedVersion(_serverVersion);
			}
			else
			{
				// Versions are equal - no action needed
				if (pendingChanges.Count == 0)
				{
					SetLastSyncedVersion(_serverVersion);
				}
			}
		}

		private Dictionary<string, object> RebuildPendingChanges()
		{
			var pendingChanges = new Dictionary<string, object>();
			var dirtyKeys = _stringListHelper.GetList();

			foreach (var key in dirtyKeys)
			{
				if (_localValueGetters.TryGetValue(key, out Func<object> getter))
				{
					var value = getter?.Invoke();
					if (value != null)
					{
						pendingChanges[key] = value;
					}
				}
			}

			return pendingChanges;
		}

		private void SendPendingChanges(Dictionary<string, object> pendingChanges)
		{
			if (_syncInProgress || _requestFactory == null ||
			    pendingChanges == null || pendingChanges.Count == 0)
			{
				return;
			}

			_syncInProgress = true;

			AbstractCommunicatorRequest request;

			if (pendingChanges.Count == 1)
			{
				var enumerator = pendingChanges.GetEnumerator();
				enumerator.MoveNext();
				var item = enumerator.Current;

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
						_stringListHelper.Clear();

						// Server bumps its version by +1 per request. After success we align local and
						// last-synced versions to serverSnapshot+1 so that subsequent server pushes
						// are not considered "older". This can lower localVersion, but the change is
						// already accepted by the server, so server is authoritative.
						_serverVersion++;

						SetLocalVersion(_serverVersion);
						SetLastSyncedVersion(_serverVersion);
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

			if (_communicator != null)
			{
				_communicator.MessageReceived -= OnMessageReceived;
			}

			foreach (var attr in _attributes.Values)
			{
				attr.Dispose();
			}

			_attributes.Clear();
			_attributeValueSetters.Clear();
			_localValueGetters.Clear();
			_serverVersionAttrKey = null;
		}
	}
}
