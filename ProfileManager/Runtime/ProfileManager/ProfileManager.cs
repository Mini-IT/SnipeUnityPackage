using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
		private readonly ISnipeCommunicator _communicator;
		private readonly AuthSubsystem _auth;
		private readonly Dictionary<string, AbstractProfileAttribute> _attributes = new();
		private readonly Dictionary<string, Action<object>> _attributeValueSetters = new();

		private readonly Dictionary<string, Func<object>> _localValueGetters = new();

		// Stores latest server snapshot values for all keys received from the server.
		// We DO NOT persist these to SharedPrefs unless the attribute is registered via GetAttribute.
		private readonly Dictionary<string, object> _serverSnapshotValues = new();

		// Stores last known server values for registered attributes.
		private readonly Dictionary<string, object> _lastSyncedServerValues = new();
		private readonly Dictionary<string, object> _dirtyLocalValues = new();
		private readonly ISharedPrefs _sharedPrefs;
		private readonly PlayerPrefsTypeHelper _prefsHelper;
		private bool _syncInProgress;
		private bool _disposed;
		private int _serverVersion;
		private string _serverVersionAttrKey;
		private bool _initialSnapshotReceived;

		public ProfileManager(IRequestFactory requestFactory, ISnipeCommunicator communicator, AuthSubsystem auth, ISharedPrefs sharedPrefs)
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
							bool updateVersions = ApplyServerAttributeChange(key, val);
							if (updateVersions)
							{
								// We just accepted server state as authoritative, so local versions are now in sync.
								SetLocalVersion(_serverVersion);
								SetLastSyncedVersion(_serverVersion);
							}
						}
					}
					break;
			}
		}

		public ProfileAttribute<T> GetAttribute<T>(SnipeApiReadOnlyUserAttribute<T> serverAttribute)
		{
			return GetAttribute(serverAttribute, default, false);
		}

		public ProfileAttribute<T> GetAttribute<T>(SnipeApiReadOnlyUserAttribute<T> serverAttribute, T defaultValue)
		{
			return GetAttribute(serverAttribute, defaultValue, true);
		}

		private ProfileAttribute<T> GetAttribute<T>(
			SnipeApiReadOnlyUserAttribute<T> serverAttribute,
			T defaultValue,
			bool useDefaultValue)
		{
			string key = serverAttribute.Key;
			if (_attributes.TryGetValue(key, out var attr))
			{
				return (ProfileAttribute<T>)attr;
			}

			var newAttr = new ProfileAttribute<T>(key, this, _sharedPrefs);
			_attributes[key] = newAttr;
			_attributeValueSetters[key] = (val) => newAttr.SetValueFromServer(TypeConverter.Convert<T>(val));
			_localValueGetters[key] = useDefaultValue
				? (Func<object>)(() => GetLocalValue(key, defaultValue))
				: () => GetLocalValue<T>(key);

			// Initialize value from server or local storage
			InitializeAttributeValue(serverAttribute, newAttr, defaultValue, useDefaultValue);

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

		private void InitializeAttributeValue<T>(
			SnipeApiReadOnlyUserAttribute<T> serverAttr,
			ProfileAttribute<T> attr,
			T defaultValue,
			bool useDefaultValue)
		{
			string key = serverAttr.Key;

			T serverValue;
			bool serverValueExists = false;

			// Prefer the latest snapshot value from server if available.
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

				// Server-first mode: if server value is known, treat it as authoritative.
				if (_serverVersion > 0)
				{
					SetLocalVersion(_serverVersion);
					SetLastSyncedVersion(_serverVersion);
				}

				return;
			}

			// If server value is not available yet, use local storage (or explicit default).
			var localValue = useDefaultValue ? GetLocalValue(key, defaultValue) : GetLocalValue<T>(key);
			attr.SetValueFromServer(localValue);
			if (useDefaultValue && !HasLocalValue(key))
			{
				SetLocalValue(key, localValue);
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

			_dirtyLocalValues[key] = value;

			// Try to send local changes to server
			SyncWithServer();
		}

		private bool ApplyServerAttributeChange(string key, object value)
		{
			if (_disposed)
			{
				return false;
			}

			// Update ONLY registered attributes (GetAttribute must be called).
			// Server snapshot values for unregistered keys are stored in _serverSnapshotValues.
			if (!_attributeValueSetters.TryGetValue(key, out Action<object> attrValueSetter))
			{
				return false;
			}

			// Server-first mode: always accept server value for this key.
			SetLocalValue(key, value);
			attrValueSetter.Invoke(value);
			_lastSyncedServerValues[key] = value;
			_dirtyLocalValues.Remove(key);
			return true;
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

			var pendingChanges = RebuildPendingChanges();
			if (pendingChanges.Count > 0)
			{
				SendPendingChanges(pendingChanges);
				return;
			}

			SetLocalVersion(_serverVersion);
			SetLastSyncedVersion(_serverVersion);
		}

		private Dictionary<string, object> RebuildPendingChanges()
		{
			// Send dirty values first, then discover any other local-vs-server diffs for registered attributes.
			var pendingChanges = new Dictionary<string, object>();

			if (_dirtyLocalValues.Count > 0)
			{
				foreach (var dirty in _dirtyLocalValues)
				{
					string key = dirty.Key;

					// Never send version attribute.
					if (!string.IsNullOrEmpty(_serverVersionAttrKey) &&
					    string.Equals(key, _serverVersionAttrKey, StringComparison.Ordinal))
					{
						continue;
					}

					pendingChanges[key] = dirty.Value;
				}
			}

			foreach (var kvp in _localValueGetters)
			{
				string key = kvp.Key;

				if (_dirtyLocalValues.ContainsKey(key))
				{
					continue;
				}

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

				// If values are equal, no need to send anything.
				if (SnipeApiUserAttribute.AreEqual(localValue, serverValue))
				{
					continue;
				}

				pendingChanges[key] = localValue;
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
				request.Request((errorCode, data) =>
				{
					_syncInProgress = false;

					if (errorCode == "ok")
					{
						// Treat sent values as the last known synced values for those keys.
						foreach (var kvp in pendingChanges)
						{
							_lastSyncedServerValues[kvp.Key] = kvp.Value;
							_serverSnapshotValues[kvp.Key] = kvp.Value;
						}

						if (data.TryGetValue(_serverVersionAttrKey, out int serverVersion))
						{
							// Server returned the version in the response. Use it.
							_serverVersion = serverVersion;
						}
						else
						{
							// Server bumps its version by +1 per request. After success we align local and
							// last-synced versions to serverSnapshot+1 so that subsequent server pushes
							// are not considered "older". This can lower localVersion, but the change is
							// already accepted by the server, so server is authoritative.
							_serverVersion++;
						}

						foreach (var kvp in pendingChanges)
						{
							if (_dirtyLocalValues.TryGetValue(kvp.Key, out object dirtyValue) &&
							    SnipeApiUserAttribute.AreEqual(dirtyValue, kvp.Value))
							{
								_dirtyLocalValues.Remove(kvp.Key);
							}
						}

						if (_dirtyLocalValues.Count == 0)
						{
							SetLocalVersion(_serverVersion);
							SetLastSyncedVersion(_serverVersion);
						}
					}
					else
					{
						// Failure - keep dirty keys for retry
						Debug.LogWarning($"ProfileManager: attr.set/setMulti failed with error {errorCode}");
					}

					if (_dirtyLocalValues.Count > 0)
					{
						SyncWithServer();
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
			return _sharedPrefs.GetInt(KEY_LOCAL_VERSION, 1);
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

		internal T GetLocalValue<T>(string key, T defaultValue)
		{
			var prefsKey = KEY_ATTR_PREFIX + key;
			return _prefsHelper.GetPrefsValue<T>(prefsKey, defaultValue);
		}

		private bool HasLocalValue(string key)
		{
			var prefsKey = KEY_ATTR_PREFIX + key;
			return _sharedPrefs.HasKey(prefsKey);
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
