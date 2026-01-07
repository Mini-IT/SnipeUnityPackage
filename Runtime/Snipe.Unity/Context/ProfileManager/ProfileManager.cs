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
		internal const string KEY_ATTR_PREFIX = "profile_attr_";

		private readonly IRequestFactory _requestFactory;
		private readonly SnipeCommunicator _communicator;
		private readonly AuthSubsystem _auth;
		private readonly Dictionary<string, AbstractProfileAttribute> _attributes = new();
		private readonly Dictionary<string, Action<object>> _attributeValueSetters = new();
		private readonly Dictionary<string, Func<object>> _localValueGetters = new();
		// Stores latest server snapshot values for all keys received from the server.
		// We DO NOT persist these to SharedPrefs unless the attribute is registered via GetAttribute.
		private readonly Dictionary<string, object> _serverSnapshotValues = new();
		// Stores last known server values for registered attributes at the moment we considered them synced.
		// Used to detect whether the server actually changed a dirty key while we were offline.
		private readonly Dictionary<string, object> _lastSyncedServerValues = new();
		private readonly ISharedPrefs _sharedPrefs;
		private readonly PlayerPrefsTypeHelper _prefsHelper;
		private bool _syncInProgress;
		private bool _disposed;
		private int _serverVersion;
		private string _serverVersionAttrKey;
		private bool _initialSnapshotReceived;

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

			_prefsHelper = new PlayerPrefsTypeHelper(_sharedPrefs);
		}

		public void Initialize(SnipeApiReadOnlyUserAttribute<int> versionAttr)
		{
			if (_serverVersionAttrKey != null)
			{
				Dispose();
			}
			_disposed = false;
			_initialSnapshotReceived = false;

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
					// Mark snapshot as received BEFORE applying, because ApplyAttributeList may
					// trigger SyncWithServer (on version update) and SyncWithServer is gated by this flag.
					_initialSnapshotReceived = true;
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

			// If there are pending changes from a previous session (dirty keys), we can only rebuild
			// them after the attribute is created and its local getter is registered.
			SyncWithServer();

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

			// Prefer server snapshot if we already received it (even if serverAttr wasn't registered
			// at the moment of attr.getAll/attr.changed and thus never became initialized).
			if (_serverVersion >= localVersion)
			{
				T serverValue;
				bool serverValueExists = false;

				if (_serverSnapshotValues.TryGetValue(key, out object rawServerValue))
				{
					serverValue = TypeConverter.Convert<T>(rawServerValue);
					serverValueExists = true;
				}
				else if (serverAttr.IsInitialized)
				{
					serverValue = serverAttr.GetValue();
					serverValueExists = true;
				}
				else
				{
					serverValue = default;
				}

				if (serverValueExists)
				{
					attr.SetValueFromServer(serverValue);
					SetLocalValue(key, serverValue);

					_lastSyncedServerValues[key] = serverValue;

					// We just accepted the server snapshot as authoritative, so local is now in sync.
					SetLocalVersion(_serverVersion);
					SetLastSyncedVersion(_serverVersion);
					return;
				}
			}

			// If we don't have the value from server yet (offline or not initialized),
			// rely on local storage to avoid overwriting with default(T).
			if (!serverAttr.IsInitialized)
			{
				var localValue = GetLocalValue<T>(key);
				attr.SetValueFromServer(localValue);
				return;
			}

			if (_serverVersion >= localVersion)
			{
				// Server has newer (or equal) version
				var serverValue = serverAttr.GetValue();
				attr.SetValueFromServer(serverValue);
				SetLocalValue(key, serverValue);

				_lastSyncedServerValues[key] = serverValue;

				// We just accepted the server snapshot as authoritative, so local is now in sync.
				SetLocalVersion(_serverVersion);
				SetLastSyncedVersion(_serverVersion);
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

			// Try to send local changes to server
			SyncWithServer();
		}

		private void ApplyServerAttributeChange(string key, object value)
		{
			if (_disposed)
			{
				return;
			}

			// Update ONLY registered attributes (GetAttribute must be called).
			// Server snapshot values for unregistered keys are stored in _serverSnapshotValues.
			if (!_attributeValueSetters.TryGetValue(key, out Action<object> attrValueSetter))
			{
				return;
			}

			var localVersion = GetLocalVersion();
			var lastSyncedVersion = GetLastSyncedVersion();

			// Ignore stale server pushes (older than last synced snapshot)
			if (_serverVersion <= lastSyncedVersion)
			{
				return;
			}

			// If local value diverged from what we last considered synced, don't blindly overwrite it just because
			// _version is global and can advance due to other keys, reconnects, etc.
			// We only accept server overwrite if the server value for this key has changed since last sync.
			if (_lastSyncedServerValues.TryGetValue(key, out object lastServerValue) &&
			    _localValueGetters.TryGetValue(key, out Func<object> localGetter))
			{
				var localValue = localGetter?.Invoke();
				bool localChangedSinceSync = !SnipeApiUserAttribute.AreEqual(localValue, lastServerValue);

				if (localChangedSinceSync && SnipeApiUserAttribute.AreEqual(lastServerValue, value))
				{
					// Server still has the last synced value -> keep local dirty value.
					return;
				}

				// If local changed since sync and server value differs from last synced value,
				// server changed this key elsewhere -> accept server as authoritative.
			}

			// Server is authoritative only when its snapshot version is greater than the local version.
			// Otherwise, preserve local offline progress.
			if (_serverVersion >= localVersion)
			{
				// Server value is authoritative - accept it
				// Update local storage with server value
				SetLocalValue(key, value);

				// Update attribute value
				attrValueSetter.Invoke(value);

				_lastSyncedServerValues[key] = value;

				// We just accepted server state as authoritative, so local versions are now in sync.
				SetLocalVersion(_serverVersion);
				SetLastSyncedVersion(_serverVersion);
			}
			else
			{
				// We might have local changes that are newer - keep them and ensure they stay in dirty keys
				// The local value is already correct, so we don't need to update it
				// Dirty keys will remain so the local value can be synced to server
			}

			// Versions are updated above when accepting server state.
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
						// Store snapshot for late-created attributes (do NOT persist to prefs here).
						_serverSnapshotValues[key] = val;
						ApplyServerAttributeChange(key, val);
					}
				}
			}

			SyncWithServer();
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
			// Never send or reconcile until we received the initial server snapshot.
			// This protects against races where local changes happen before attr.getAll arrives.
			if (_syncInProgress || _serverVersion == 0 || !_initialSnapshotReceived)
			{
				return;
			}

			int localVersion = GetLocalVersion();
			int lastSyncedVersion = GetLastSyncedVersion();

			if (localVersion > lastSyncedVersion)
			{
				// Rebuild pending changes
				var pendingChanges = RebuildPendingChanges();

				// Client has unsynced changes
				// If nothing is registered yet, pendingChanges can be empty even when prefs contain dirty keys.
				// Don't advance lastSynced in this case - just wait for attributes to be registered.
				if (pendingChanges.Count == 0)
				{
					return;
				}
				SendPendingChanges(pendingChanges);
			}
			else if (_serverVersion > localVersion)
			{
				// Server has newer changes - accept all server values.
				// Values should be handled by incoming messages.
				SetLocalVersion(_serverVersion);
				SetLastSyncedVersion(_serverVersion);
			}
			else
			{
				// Rebuild pending changes
				var pendingChanges = RebuildPendingChanges();

				if (pendingChanges.Count == 0)
				{
					SetLastSyncedVersion(_serverVersion);
				}
			}
		}

		private Dictionary<string, object> RebuildPendingChanges()
		{
			// Diff registered local values vs the last received server snapshot.
			// ProfileManager operates only on registered attributes.
			var pendingChanges = new Dictionary<string, object>();

			foreach (var kvp in _localValueGetters)
			{
				string key = kvp.Key;

				// Never send version attribute.
				if (!string.IsNullOrEmpty(_serverVersionAttrKey) &&
				    string.Equals(key, _serverVersionAttrKey, StringComparison.Ordinal))
				{
					continue;
				}

				// If we don't have this key in snapshot yet, we can't reliably decide.
				if (!_serverSnapshotValues.TryGetValue(key, out object serverValue))
				{
					continue;
				}

				var localValue = kvp.Value?.Invoke();
				if (!SnipeApiUserAttribute.AreEqual(localValue, serverValue))
				{
					pendingChanges[key] = localValue;
				}
			}

			return pendingChanges;
		}

		private bool CheckCommunicatorLoggedIn()
		{
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
			if (_forceLoggedInForTests)
			{
				return true;
			}
#endif
			return _communicator != null && _communicator.LoggedIn;
		}

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
		private bool _forceLoggedInForTests;

		/// <summary>
		/// Test-only helper to simulate login state without establishing a real connection.
		/// </summary>
		internal void ForceLoggedInForTests(bool loggedIn)
		{
			_forceLoggedInForTests = loggedIn;
		}
#endif

		private void SendPendingChanges(Dictionary<string, object> pendingChanges)
		{
			if (_syncInProgress || _requestFactory == null || !CheckCommunicatorLoggedIn() ||
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
						// Treat sent values as the last synced server values for those keys.
						foreach (var kvp in pendingChanges)
						{
							_lastSyncedServerValues[kvp.Key] = kvp.Value;
							_serverSnapshotValues[kvp.Key] = kvp.Value;
						}

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
			_serverSnapshotValues.Clear();
			_lastSyncedServerValues.Clear();
			_serverVersionAttrKey = null;
		}
	}
}
