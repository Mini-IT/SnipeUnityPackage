using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using MiniIT.Snipe;
using MiniIT.Snipe.Api;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestProfileManager
	{
		private ProfileManager _profileManager;
		private MockSnipeApiService _mockApiService;
		private MockSnipeApiUserAttributes _mockUserAttributes;
		private MockSnipeApiReadOnlyUserAttribute<int> _mockVersionAttribute;
		private MockSharedPrefs _mockSharedPrefs;

		[SetUp]
		public void SetUp()
		{
			// Create mock shared prefs
			_mockSharedPrefs = new MockSharedPrefs();

			_mockApiService = new MockSnipeApiService();
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);
			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			// Initialize version attribute with 1 so ValueChanged events will be raised on subsequent SetValue calls
			SetLocalVersion(1);
			_mockVersionAttribute.SetValue(1);
			_mockUserAttributes.RegisterAttribute(_mockVersionAttribute);

			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
		}

		[TearDown]
		public void TearDown()
		{
			_profileManager?.Dispose();
		}

		[Test]
		public void GetAttribute_NewAttribute_ReturnsProfileAttribute()
		{
			// Arrange & Act
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Assert
			Assert.IsNotNull(attr);
			Assert.AreEqual(0, attr.Value);
		}

		[Test]
		public void GetAttribute_ExistingAttribute_ReturnsSameInstance()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr1 = _profileManager.GetAttribute<int>(serverAttr);

			// Act
			var attr2 = _profileManager.GetAttribute<int>(serverAttr);

			// Assert
			Assert.AreSame(attr1, attr2);
		}

		[Test]
		public void GetAttribute_ServerAttributeInitialized_UsesServerValue()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetValue(100);
			_mockUserAttributes.RegisterAttribute(serverAttr);

			// Act
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Assert
			Assert.AreEqual(100, attr.Value);
		}

		[Test]
		public void OnLocalAttributeChanged_IncrementsVersion()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			var initialVersion = GetLocalVersion();

			// Act
			attr.Value = 100;

			// Assert
			var newVersion = GetLocalVersion();
			Assert.AreEqual(initialVersion + 1, newVersion);
		}

		[Test]
		public void OnLocalAttributeChanged_SavesToLocalStorage()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Act
			attr.Value = 100;

			// Assert
			Assert.AreEqual(100, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
		}

		[Test]
		public void OnServerAttributeChanged_UpdatesLocalStorage()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetValue(50);
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Act
			_profileManager.HandleServerMessage("attr.changed", "ok", new Dictionary<string, object>()
			{
				["list"] = new List<IDictionary<string, object>>()
				{
					// attr.changed should also carry updated _version; without it ProfileManager
					// can't safely decide if the server value is newer than local.
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 2
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 200
					}
				}
			}, 0);

			// Assert
			Assert.AreEqual(200, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.AreEqual(200, attr.Value);
		}

		[Test]
		public void OnServerAttributeChanged_MultipleAttributesInOneMessage_AllAreApplied()
		{
			// Arrange
			var coinsServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			var gemsServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "gems");
			_mockUserAttributes.RegisterAttribute(coinsServerAttr);
			_mockUserAttributes.RegisterAttribute(gemsServerAttr);

			var coinsAttr = _profileManager.GetAttribute<int>(coinsServerAttr);
			var gemsAttr = _profileManager.GetAttribute<int>(gemsServerAttr);

			// Act - one server message contains multiple attributes with the same _version
			_profileManager.HandleServerMessage("attr.changed", "ok", new Dictionary<string, object>()
			{
				["list"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 2
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 200
					},
					new Dictionary<string, object>()
					{
						["key"] = "gems",
						["val"] = 15
					}
				}
			}, 0);

			// Assert - both values must be applied
			Assert.AreEqual(200, coinsAttr.Value);
			Assert.AreEqual(15, gemsAttr.Value);

			Assert.AreEqual(200, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.AreEqual(15, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "gems", 0));
		}

		[Test]
		public void SyncWithServer_LocalVersionGreater_SendsPendingChangesSingle()
		{
			// Arrange - simulate a previous session where attribute was used and dirty keys were created
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			_mockVersionAttribute.SetValue(1);

			// Set up state as if from a previous session: local version > last synced version, with local values
			SetLocalVersion(5);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 100);
			_mockSharedPrefs.Save();
			_mockVersionAttribute.SetValue(4);
			_mockApiService.SetNextRequestSuccess(true);
			var initialRequestCount = _mockApiService.RequestCount;

			// Act - create new ProfileManager, initialize, and retrieve attribute
			// Retrieving the attribute registers the local value getter, enabling RebuildPendingChanges to work
			_profileManager.Dispose();
			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			_profileManager.ForceLoggedInForTests(true);
			// Get attribute to register local value getter so RebuildPendingChanges can find the value
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Initial server snapshot must be received before we can send anything.
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 4
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 50
					}
				}
			}, 0);
			// Local coins (100) differs from server snapshot (50), so pending changes should be sent now.

			// Assert - should attempt to send pending changes when local version > last synced version
			Assert.Greater(_mockApiService.RequestCount, initialRequestCount,
				"Request should be made when local version is greater than last synced version");
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType,
				"Request type should be attr.set for syncing pending changes");
			Assert.AreEqual("coins", _mockApiService.LastRequestData["key"]);
			Assert.AreEqual(100, _mockApiService.LastRequestData["val"]);
		}

		[Test]
		public void SyncWithServer_ServerVersionGreater_AcceptsServerValues()
		{
			// Arrange
			SetLocalVersion(3);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);

			// Act
			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);

			// Initial snapshot arrives
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 5
					}
				}
			}, 0);

			// Assert
			var localVersion = GetLocalVersion();
			Assert.AreEqual(5, localVersion);
		}

		[Test]
		public void LocalChangeDuringSync_IsNotLost_AndResyncsAfterCompletion()
		{
			_profileManager.Dispose();

			SetLocalVersion(1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 1);

			var delayedService = new DelayedMockSnipeApiService();
			delayedService.AutoComplete = false;
			delayedService.SetNextRequestSuccess(true);

			_mockUserAttributes = new MockSnipeApiUserAttributes(delayedService);
			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(delayedService, "_version");
			_mockVersionAttribute.SetValue(1);
			_mockUserAttributes.RegisterAttribute(_mockVersionAttribute);

			_profileManager = new ProfileManager(delayedService, delayedService.Communicator, delayedService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			_profileManager.ForceLoggedInForTests(true);

			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(delayedService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			attr.Value = 11;

			Assert.AreEqual(1, delayedService.RequestCount);
			Assert.IsNotNull(delayedService.PendingCallback);

			attr.Value = 12;

			delayedService.AutoComplete = true;
			delayedService.PendingCallback.Invoke("ok", new Dictionary<string, object>());

			Assert.AreEqual(2, delayedService.RequestCount);
			Assert.AreEqual("attr.set", delayedService.LastRequestType);
			Assert.AreEqual("coins", delayedService.LastRequestData["key"]);
			Assert.AreEqual(12, delayedService.LastRequestData["val"]);
		}

		[Test]
		public void Scenario1_OnlineThenOfflineThenReconnect_OlderServerSnapshot_DoesNotOverwrite_SyncsToServer()
		{
			// Initial:
			// server: { _version = 1; coins = 10 }
			// local:  { _version = 1; coins = 10 }
			//
			// Act:
			// go offline -> set coins=20 -> localVersion=2, keep dirty
			// reconnect -> receive attr.getAll { _version=1; coins=10 }
			//
			// Assert:
			// coins == 20, ValueChanged not called due to snapshot, local value sent, versions aligned to server+1.

			// Arrange
			SetLocalVersion(1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 1);

			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetValue(10);
			_mockUserAttributes.RegisterAttribute(serverAttr);
			_mockVersionAttribute.SetValue(1);
			_mockApiService.SetNextRequestSuccess(true);

			var attr = _profileManager.GetAttribute<int>(serverAttr);
			Assert.AreEqual(10, attr.Value);

			int valueChangedCount = 0;
			attr.ValueChanged += _ => valueChangedCount++;

			// Act 1: go offline and change value
			_profileManager.ForceLoggedInForTests(false);
			_mockApiService.SetNextRequestSuccess(false);
			attr.Value = 20;
			Assert.AreEqual(20, attr.Value);
			Assert.AreEqual(1, valueChangedCount, "Local change must raise ValueChanged exactly once");

			int requestCountBeforeReconnect = _mockApiService.RequestCount;

			// Act 2: reconnect and receive old snapshot (attr.getAll), then sync succeeds
			_profileManager.ForceLoggedInForTests(true);
			_mockApiService.SetNextRequestSuccess(true);
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			// Assert - Local offline changes should be preserved
			Assert.AreEqual(20, attr.Value);
			Assert.AreEqual(1, valueChangedCount, "Snapshot should not raise ValueChanged when resolved value didn't change");

			Assert.Greater(_mockApiService.RequestCount, requestCountBeforeReconnect,
				"Reconnect snapshot must trigger sending pending local changes");
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual("coins", _mockApiService.LastRequestData["key"]);
			Assert.AreEqual(20, _mockApiService.LastRequestData["val"]);

			Assert.AreEqual(20, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins"));
			Assert.AreEqual(2, _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION));
			Assert.AreEqual(2, _mockSharedPrefs.GetInt(ProfileManager.KEY_LAST_SYNCED_VERSION));
		}

		[Test]
		public void Regression_ReconnectSnapshotHasOldValueButHigherVersion_DoesNotOverwriteDirtyKey()
		{
			// Repro:
			// - online, snapshot: coins=61, _version=896
			// - coins++ -> 62, attr.set succeeds
			// - offline, coins++ -> 63, no send
			// - reconnect snapshot: coins=62, _version=899
			// BUG: coins becomes 62. Expected: keep 63 and send it.

			_profileManager.Dispose();

			SetLocalVersion(0);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 0);

			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);
			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			_profileManager.ForceLoggedInForTests(true);

			// Initial server snapshot arrives before the coins attribute is initialized
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 61
					},
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 896
					},
				}
			}, 0);

			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins"); // not initialized
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			Assert.AreEqual(61, attr.Value);

			// Online change -> should send and succeed
			_mockApiService.SetNextRequestSuccess(true);
			attr.Value = 62;
			Assert.AreEqual(62, attr.Value);
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual(62, _mockApiService.LastRequestData["val"]);

			// Offline change -> no send
			_profileManager.ForceLoggedInForTests(false);
			_mockApiService.SetNextRequestSuccess(true);
			attr.Value = 63;
			Assert.AreEqual(63, attr.Value);

			int requestCountBeforeReconnect = _mockApiService.RequestCount;

			// Reconnect snapshot: old coins value but higher version
			_profileManager.ForceLoggedInForTests(true);
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 62
					},
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 899
					},
				}
			}, 0);

			// Must keep local dirty value and send it
			Assert.AreEqual(63, attr.Value);
			Assert.Greater(_mockApiService.RequestCount, requestCountBeforeReconnect);
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual(63, _mockApiService.LastRequestData["val"]);
		}

		[Test]
		public void Scenario2_OfflineStart_LocalKnown_ThenReconnect_OlderServerSnapshot_DoesNotOverwrite_SyncsToServer()
		{
			// Initial:
			// offline
			// local:  { _version = 1; coins = 10 }
			//
			// Act:
			// set coins=20 -> localVersion=2
			// receive attr.getAll snapshot { _version=1; coins=10 }
			//
			// Assert:
			// coins == 20, snapshot doesn't raise ValueChanged, local value sent, versions aligned.

			// Arrange - start offline: server version attribute is NOT initialized
			_profileManager.Dispose();
			SetLocalVersion(1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 10);

			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);

			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);

			// Offline: server attribute value isn't initialized
			_profileManager.ForceLoggedInForTests(false);
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);

			var attr = _profileManager.GetAttribute<int>(serverAttr);
			Assert.AreEqual(10, attr.Value);

			int valueChangedCount = 0;
			attr.ValueChanged += _ => valueChangedCount++;

			_mockApiService.SetNextRequestSuccess(false);
			attr.Value = 20;
			Assert.AreEqual(1, valueChangedCount);

			int requestCountBeforeReconnect = _mockApiService.RequestCount;

			_profileManager.ForceLoggedInForTests(true);
			_mockApiService.SetNextRequestSuccess(true);
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			Assert.AreEqual(20, attr.Value);
			Assert.AreEqual(1, valueChangedCount, "Snapshot should not raise ValueChanged when resolved value didn't change");

			Assert.Greater(_mockApiService.RequestCount, requestCountBeforeReconnect);
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual("coins", _mockApiService.LastRequestData["key"]);
			Assert.AreEqual(20, _mockApiService.LastRequestData["val"]);

			Assert.AreEqual(20, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins"));
			Assert.AreEqual(2, _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION));
			Assert.AreEqual(2, _mockSharedPrefs.GetInt(ProfileManager.KEY_LAST_SYNCED_VERSION));
		}

		[Test]
		public void Scenario3_OnlineStart_LocalNotInitialized_TakesServerValue_AndPersistsLocally()
		{
			// Initial:
			// online
			// server: { _version = 1; coins = 10 }
			// local:  { not initialized }
			//
			// Assert:
			// coins == 10, local value/version are set to server ones.

			_profileManager.Dispose();

			SetLocalVersion(0);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 0);
			_mockSharedPrefs.DeleteKey(ProfileManager.KEY_ATTR_PREFIX + "coins");

			// Online but BEFORE the first snapshot: server version attribute isn't initialized yet.
			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);

			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);

			// Create attribute BEFORE server snapshot arrives, and subscribe to changes.
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins"); // not initialized
			_mockUserAttributes.RegisterAttribute(serverAttr);

			var attr = _profileManager.GetAttribute<int>(serverAttr);

			int valueChangedCount = 0;
			int lastValueChanged = 0;
			attr.ValueChanged += v =>
			{
				valueChangedCount++;
				lastValueChanged = v;
			};

			var observer = new TestObserver<int>();
			attr.Subscribe(observer);

			// Act: first server snapshot comes in
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			// Assert
			Assert.AreEqual(10, attr.Value);
			Assert.AreEqual(1, valueChangedCount);
			Assert.AreEqual(10, lastValueChanged);
			Assert.AreEqual(1, observer.NextCount);
			Assert.AreEqual(10, observer.LastValue);

			Assert.AreEqual(10, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins"));
			Assert.AreEqual(1, _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION));
			Assert.AreEqual(1, _mockSharedPrefs.GetInt(ProfileManager.KEY_LAST_SYNCED_VERSION));
		}

		[Test]
		public void Scenario6_OfflinePendingChange_ThenChangedSnapshotWithoutKey_StillSyncsLocalChange()
		{
			// Initial:
			// offline
			// local: { _version = 1; coins = 10 }
			//
			// Act:
			// set coins=20 -> localVersion=2 (still offline)
			// receive attr.getAll { _version=1; coins=10 } while offline (no send)
			// reconnect and receive attr.changed with unrelated key (coins not included)
			//
			// Assert:
			// local coins still sent to server on reconnect even though snapshot list didn't include it.

			_profileManager.Dispose();

			SetLocalVersion(1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 10);

			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);

			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			_profileManager.ForceLoggedInForTests(false);

			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			Assert.AreEqual(10, attr.Value);

			_mockApiService.SetNextRequestSuccess(false);
			attr.Value = 20;

			int requestCountBeforeSnapshots = _mockApiService.RequestCount;

			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			Assert.AreEqual(20, attr.Value);
			Assert.AreEqual(requestCountBeforeSnapshots, _mockApiService.RequestCount);

			_profileManager.ForceLoggedInForTests(true);
			_mockApiService.SetNextRequestSuccess(true);
			_profileManager.HandleServerMessage("attr.changed", "ok", new Dictionary<string, object>()
			{
				["list"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 2
					},
					new Dictionary<string, object>()
					{
						["key"] = "gems",
						["val"] = 5
					}
				}
			}, 0);

			Assert.Greater(_mockApiService.RequestCount, requestCountBeforeSnapshots);
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual("coins", _mockApiService.LastRequestData["key"]);
			Assert.AreEqual(20, _mockApiService.LastRequestData["val"]);
		}

		[Test]
		public void Scenario3_OnlineStart_SnapshotArrivesBeforeAttributeCreation_LateCreatedAttributeReadsServerValue()
		{
			// Real-life regression:
			// attr.getAll can arrive before any ProfileAttribute is created.
			// In this case ProfileManager must still persist values to prefs,
			// so GetAttribute later returns correct server value (not default(T)).

			_profileManager.Dispose();

			SetLocalVersion(0);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 0);
			_mockSharedPrefs.DeleteKey(ProfileManager.KEY_ATTR_PREFIX + "coins");

			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);

			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);

			// Snapshot arrives first
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			// Now attribute is created (server attr object may still be uninitialized in this test)
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			Assert.AreEqual(10, attr.Value);
			Assert.AreEqual(10, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins"));
			Assert.AreEqual(1, _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION));
			Assert.AreEqual(1, _mockSharedPrefs.GetInt(ProfileManager.KEY_LAST_SYNCED_VERSION));
		}

		[Test]
		public void Scenario4_OfflineStart_LocalAhead_ThenReconnect_NewerServerSnapshot_DoesNotOverwrite_SyncsToServer()
		{
			// Initial:
			// offline
			// local:  { _version = 5; coins = 10 } (dirty)
			//
			// Act:
			// reconnect -> receive attr.getAll { _version = 2; coins = 20 }
			//
			// Assert:
			// coins == 10, snapshot doesn't raise ValueChanged, local value sent, versions aligned to server+1.

			_profileManager.Dispose();

			SetLocalVersion(5);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 2);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 10);

			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);

			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			_profileManager.ForceLoggedInForTests(false);

			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			Assert.AreEqual(10, attr.Value);

			int valueChangedCount = 0;
			attr.ValueChanged += _ => valueChangedCount++;

			int requestCountBeforeReconnect = _mockApiService.RequestCount;

			_profileManager.ForceLoggedInForTests(true);
			_mockApiService.SetNextRequestSuccess(true);
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 2
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 20
					}
				}
			}, 0);

			Assert.AreEqual(10, attr.Value);
			Assert.AreEqual(0, valueChangedCount, "Snapshot should not raise ValueChanged when resolved value didn't change");

			Assert.Greater(_mockApiService.RequestCount, requestCountBeforeReconnect);
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual("coins", _mockApiService.LastRequestData["key"]);
			Assert.AreEqual(10, _mockApiService.LastRequestData["val"]);

			Assert.AreEqual(10, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins"));
			Assert.AreEqual(3, _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION));
			Assert.AreEqual(3, _mockSharedPrefs.GetInt(ProfileManager.KEY_LAST_SYNCED_VERSION));
		}

		[Test]
		public void Scenario5_OnlineStart_LocalAhead_SendsLocalValueToServer()
		{
			// Initial:
			// online
			// server: { _version = 1; coins = 10 }
			// local:  { _version = 3; coins = 20 } (dirty)
			//
			// Assert:
			// ProfileAttribute.Value == 20 and coins=20 is sent to server.

			_profileManager.Dispose();

			SetLocalVersion(3);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 1);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 20);

			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			_mockVersionAttribute.SetValue(1);
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);

			_mockApiService.SetNextRequestSuccess(true);
			_profileManager = new ProfileManager(_mockApiService, _mockApiService.Communicator, _mockApiService.Auth, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			_profileManager.ForceLoggedInForTests(true);

			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetValue(10);
			_mockUserAttributes.RegisterAttribute(serverAttr);

			int requestCountBefore = _mockApiService.RequestCount;

			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Initial snapshot must arrive before any send attempt.
			_profileManager.HandleServerMessage("attr.getAll", "ok", new Dictionary<string, object>()
			{
				["data"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 1
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 10
					}
				}
			}, 0);

			Assert.AreEqual(20, attr.Value);
			Assert.Greater(_mockApiService.RequestCount, requestCountBefore);
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType);
			Assert.AreEqual("coins", _mockApiService.LastRequestData["key"]);
			Assert.AreEqual(20, _mockApiService.LastRequestData["val"]);
		}

		[Test]
		public void OfflineChanges_ReconnectWithNewerServerValue_AcceptsServerValue()
		{
			// Test that if server version is newer, we accept server value (normal case)

			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetValue(2000);
			_mockUserAttributes.RegisterAttribute(serverAttr);
			_mockVersionAttribute.SetValue(1);
			_mockApiService.SetNextRequestSuccess(false);

			var attr = _profileManager.GetAttribute<int>(serverAttr);
			attr.Value = 2050; // Local change while offline

			// Act - Server has newer version (someone else changed it on another device)
			_profileManager.HandleServerMessage("attr.changed", "ok", new Dictionary<string, object>()
			{
				["list"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 5
					},
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 2100
					}
				}
			}, 0);

			// Assert - Server value should be accepted when server version is newer
			Assert.AreEqual(2100, attr.Value,
				"Server value should be accepted when server version is newer");
			Assert.AreEqual(2100, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0),
				"Local storage should be updated with server value");
		}

		[Test]
		public void ProfileAttribute_ValueChanged_EventFires()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			bool eventFired = false;
			int eventValue = 0;
			attr.ValueChanged += (value) =>
			{
				eventFired = true;
				eventValue = value;
			};

			// Act
			attr.Value = 100;

			// Assert
			Assert.IsTrue(eventFired);
			Assert.AreEqual(100, eventValue);
		}

		[Test]
		public void ProfileAttribute_SetValueFromServer_DoesNotTriggerLocalChange()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			int changeCount = 0;
			attr.ValueChanged += (value) => changeCount++;

			// Act
			attr.SetValueFromServer(100);

			// Assert
			Assert.AreEqual(1, changeCount); // ValueChanged should fire
			// But OnLocalAttributeChanged should NOT be called (local version should not change)
			Assert.AreEqual(1, _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION));
		}

		[Test]
		public void Dispose_UnsubscribesFromEvents()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Act
			_profileManager.Dispose();

			// Assert
			// Verify no exceptions when server attribute changes after dispose
			Assert.DoesNotThrow(() => _profileManager.HandleServerMessage("attr.changed", "ok", new Dictionary<string, object>()
			{
				["list"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "coins",
						["val"] = 100
					}
				}
			}, 0));
		}

		[Test]
		public void DifferentAttributeTypes_HandledCorrectly()
		{
			// Arrange & Act
			var intServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			var floatServerAttr = new MockSnipeApiReadOnlyUserAttribute<float>(_mockApiService, "score");
			var boolServerAttr = new MockSnipeApiReadOnlyUserAttribute<bool>(_mockApiService, "enabled");
			var stringServerAttr = new MockSnipeApiReadOnlyUserAttribute<string>(_mockApiService, "name");
			_mockUserAttributes.RegisterAttribute(intServerAttr);
			_mockUserAttributes.RegisterAttribute(floatServerAttr);
			_mockUserAttributes.RegisterAttribute(boolServerAttr);
			_mockUserAttributes.RegisterAttribute(stringServerAttr);
			var intAttr = _profileManager.GetAttribute<int>(intServerAttr);
			var floatAttr = _profileManager.GetAttribute<float>(floatServerAttr);
			var boolAttr = _profileManager.GetAttribute<bool>(boolServerAttr);
			var stringAttr = _profileManager.GetAttribute<string>(stringServerAttr);

			intAttr.Value = 100;
			floatAttr.Value = 3.14f;
			boolAttr.Value = true;
			stringAttr.Value = "test";

			// Assert
			Assert.AreEqual(100, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.AreEqual(3.14f, _mockSharedPrefs.GetFloat(ProfileManager.KEY_ATTR_PREFIX + "score", 0f), 0.001f);
			Assert.AreEqual(1, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "enabled", 0));
			Assert.AreEqual("test", _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "name", ""));
		}

		[Test]
		public void ListAttributeTypes_HandledCorrectly()
		{
			// Arrange & Act
			var intListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<int>>(_mockApiService, "intList");
			var floatListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<float>>(_mockApiService, "floatList");
			var boolListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<bool>>(_mockApiService, "boolList");
			var stringListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<string>>(_mockApiService, "stringList");
			_mockUserAttributes.RegisterAttribute(intListServerAttr);
			_mockUserAttributes.RegisterAttribute(floatListServerAttr);
			_mockUserAttributes.RegisterAttribute(boolListServerAttr);
			_mockUserAttributes.RegisterAttribute(stringListServerAttr);
			var intListAttr = _profileManager.GetAttribute<List<int>>(intListServerAttr);
			var floatListAttr = _profileManager.GetAttribute<List<float>>(floatListServerAttr);
			var boolListAttr = _profileManager.GetAttribute<List<bool>>(boolListServerAttr);
			var stringListAttr = _profileManager.GetAttribute<List<string>>(stringListServerAttr);

			var intList = new List<int> { 1, 2, 3 };
			var floatList = new List<float> { 1.1f, 2.2f, 3.3f };
			var boolList = new List<bool> { true, false, true };
			var stringList = new List<string> { "a", "b", "c" };

			intListAttr.Value = intList;
			floatListAttr.Value = floatList;
			boolListAttr.Value = boolList;
			stringListAttr.Value = stringList;

			// Assert - Check that the values are stored correctly
			var storedIntListStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "intList", "");
			var storedFloatListStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "floatList", "");
			var storedBoolListStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "boolList", "");
			var storedStringListStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "stringList", "");

			Assert.AreEqual("\"1\";\"2\";\"3\"", storedIntListStr);
			Assert.AreEqual("\"1.1\";\"2.2\";\"3.3\"", storedFloatListStr);
			Assert.AreEqual("\"True\";\"False\";\"True\"", storedBoolListStr);
			Assert.AreEqual("\"a\";\"b\";\"c\"", storedStringListStr);

			// Verify that the attributes return the correct values
			CollectionAssert.AreEqual(intList, intListAttr.Value);
			CollectionAssert.AreEqual(floatList, floatListAttr.Value);
			CollectionAssert.AreEqual(boolList, boolListAttr.Value);
			CollectionAssert.AreEqual(stringList, stringListAttr.Value);
		}

		[Test]
		public void ListAttribute_EmptyList_HandledCorrectly()
		{
			// Arrange & Act
			var intListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<int>>(_mockApiService, "emptyIntList");
			_mockUserAttributes.RegisterAttribute(intListServerAttr);
			var intListAttr = _profileManager.GetAttribute<List<int>>(intListServerAttr);

			var emptyList = new List<int>();
			intListAttr.Value = emptyList;

			// Assert
			var storedStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "emptyIntList", "");
			Assert.AreEqual("", storedStr);
			CollectionAssert.AreEqual(emptyList, intListAttr.Value);
		}

		[Test]
		public void ListAttribute_NullList_HandledCorrectly()
		{
			// Arrange & Act
			var intListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<int>>(_mockApiService, "nullIntList");
			_mockUserAttributes.RegisterAttribute(intListServerAttr);
			var intListAttr = _profileManager.GetAttribute<List<int>>(intListServerAttr);

			intListAttr.Value = null;

			// Assert
			var storedStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "nullIntList", "");
			Assert.AreEqual("", storedStr);
			Assert.IsNull(intListAttr.Value);
		}

		[Test]
		public void ListAttribute_ServerValue_UpdatesLocalValue()
		{
			// Arrange
			var intListServerAttr = new MockSnipeApiReadOnlyUserAttribute<List<int>>(_mockApiService, "serverIntList");
			_mockUserAttributes.RegisterAttribute(intListServerAttr);
			var intListAttr = _profileManager.GetAttribute<List<int>>(intListServerAttr);

			var serverList = new List<int> { 10, 20, 30 };

			// Act - Simulate server sending value
			_profileManager.HandleServerMessage("attr.changed", "ok", new Dictionary<string, object>()
			{
				["list"] = new List<IDictionary<string, object>>()
				{
					new Dictionary<string, object>()
					{
						["key"] = "_version",
						["val"] = 2
					},
					new Dictionary<string, object>()
					{
						["key"] = "serverIntList",
						["val"] = serverList
					}
				}
			}, 0);

			// Assert
			CollectionAssert.AreEqual(serverList, intListAttr.Value);

			// Verify it's stored locally
			var storedStr = _mockSharedPrefs.GetString(ProfileManager.KEY_ATTR_PREFIX + "serverIntList", "");
			Assert.AreEqual("\"10\";\"20\";\"30\"", storedStr);
		}

		[Test]
		public void ListAttribute_StringsWithSpecialCharacters_WorkCorrectly()
		{
			// Arrange
			var stringListLocalAttr = new LocalProfileAttribute<List<string>>("specialStringList", _mockSharedPrefs);

			var specialStrings = new List<string> { "hello;world", "test\\backslash", "normal", "with spaces", "" };

			// Act
			stringListLocalAttr.Value = specialStrings;

			// Assert - Verify the attribute returns correct values
			CollectionAssert.AreEqual(specialStrings, stringListLocalAttr.Value);

			// Verify storage format uses quoted strings
			var storedStr = _mockSharedPrefs.GetString("specialStringList", "");
			// Should be: "\"hello;world\";\"test\\\\backslash\";\"normal\";\"with spaces\";\"\""
			Assert.AreEqual("\"hello;world\";\"test\\\\backslash\";\"normal\";\"with spaces\";\"\"", storedStr);

			// Verify round-trip: create new attribute and check it reads back correctly
			var newStringListAttr = new LocalProfileAttribute<List<string>>("specialStringList", _mockSharedPrefs);
			CollectionAssert.AreEqual(specialStrings, newStringListAttr.Value);
		}

		[Test]
		public void Migrate_OldKeyExists_MigratesValue()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			var oldKey = ProfileManager.KEY_ATTR_PREFIX + "old_coins";
			var oldValue = 250;
			_mockSharedPrefs.SetInt(oldKey, oldValue);
			_mockSharedPrefs.Save();

			// Act
			attr.Migrate(oldKey);

			// Assert
			Assert.AreEqual(oldValue, attr.Value);
		}

		[Test]
		public void Migrate_OldKeyExists_MigratesValueAndDeletesOldKey()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			var oldKey = "old_prefix_" + ProfileManager.KEY_ATTR_PREFIX + "coins";
			var oldValue = 250;
			_mockSharedPrefs.SetInt(oldKey, oldValue);
			_mockSharedPrefs.Save();

			// Act
			attr.Migrate(oldKey);

			// Assert
			int storedValue = _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0);
			Assert.AreEqual(oldValue, storedValue);
			Assert.IsFalse(_mockSharedPrefs.HasKey(oldKey));
		}

		[Test]
		public void Migrate_OldKeyDoesNotExist_DoesNothing()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			var initialValue = 100;
			attr.Value = initialValue;
			var nonExistentOldKey = ProfileManager.KEY_ATTR_PREFIX + "nonexistent_key";

			// Act
			attr.Migrate(nonExistentOldKey);

			// Assert
			Assert.AreEqual(initialValue, attr.Value);
			Assert.AreEqual(initialValue, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.IsFalse(_mockSharedPrefs.HasKey(nonExistentOldKey));
		}

		[Test]
		public void LocalProfileAttribute_ListTypes_WorkCorrectly()
		{
			// Arrange
			var intListLocalAttr = new LocalProfileAttribute<List<int>>("localIntList", _mockSharedPrefs);
			var stringListLocalAttr = new LocalProfileAttribute<List<string>>("localStringList", _mockSharedPrefs);

			var intList = new List<int> { 5, 10, 15 };
			var stringList = new List<string> { "hello", "world" };

			// Act
			intListLocalAttr.Value = intList;
			stringListLocalAttr.Value = stringList;

			// Assert - Check storage
			var storedIntListStr = _mockSharedPrefs.GetString("localIntList", "");
			var storedStringListStr = _mockSharedPrefs.GetString("localStringList", "");

			Assert.AreEqual("\"5\";\"10\";\"15\"", storedIntListStr);
			Assert.AreEqual("\"hello\";\"world\"", storedStringListStr);

			// Verify retrieval
			var retrievedIntList = new LocalProfileAttribute<List<int>>("localIntList", _mockSharedPrefs).Value;
			var retrievedStringList = new LocalProfileAttribute<List<string>>("localStringList", _mockSharedPrefs).Value;

			CollectionAssert.AreEqual(intList, retrievedIntList);
			CollectionAssert.AreEqual(stringList, retrievedStringList);
		}

		private ProfileAttribute<T> CreateAttribute<T>(string key, AbstractSnipeApiService service = null)
		{
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<T>(service ?? _mockApiService, key);
			_mockUserAttributes.RegisterAttribute(serverAttr);
			return _profileManager.GetAttribute<T>(serverAttr);
		}

		private int GetLocalVersion() => _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION, 0);
		private void SetLocalVersion(int value) => _mockSharedPrefs.SetInt(ProfileManager.KEY_LOCAL_VERSION, value);
	}

	internal class MockAuthSubsystem : AuthSubsystem
	{
		public MockAuthSubsystem(SnipeOptions options, ISnipeCommunicator communicator, ISnipeServices services)
			: base(0, options, communicator, null, services)
		{
			// Initialize with contextId=0, communicator, and null analytics for testing
		}

		public override void Dispose()
		{
			// No-op for testing
		}

		protected override UniTaskVoid RegisterAndLogin()
		{
			// No-op for testing
			return default;
		}
	}

	internal class MockSnipeApiService : AbstractSnipeApiService
	{
		public string LastRequestType { get; private set; }
		public IDictionary<string, object> LastRequestData { get; private set; }
		public int RequestCount { get; private set; }
		public ISnipeCommunicator Communicator => _mockCommunicator;
		public AuthSubsystem Auth => _mockAuth;

		private static MockAuthSubsystem s_tempMockAuth;
		private static ISnipeServices s_tempServices;
		private static SnipeOptions s_tempOptions;

		protected bool _nextRequestSuccess = false; // Default to false so dirty keys persist for testing
		protected readonly ISnipeCommunicator _mockCommunicator;
		protected readonly MockAuthSubsystem _mockAuth;

		public MockSnipeApiService() : this(CreateMockCommunicator())
		{
		}

		private static ISnipeCommunicator CreateMockCommunicator()
		{
			s_tempServices = new NullSnipeServices();
			s_tempOptions = new SnipeOptions(0, new SnipeOptionsData(), s_tempServices);
			return new SnipeCommunicator(s_tempOptions, null, s_tempServices);
		}

		private MockSnipeApiService(ISnipeCommunicator communicator) : base(communicator, CreateMockAuth(communicator))
		{
			_mockCommunicator = _communicator;
			_mockAuth = s_tempMockAuth;
			s_tempMockAuth = null;
		}

		private static MockAuthSubsystem CreateMockAuth(ISnipeCommunicator communicator)
		{
			s_tempMockAuth ??= new MockAuthSubsystem(s_tempOptions, communicator, s_tempServices);
			return s_tempMockAuth;
		}

		public void SetNextRequestSuccess(bool success)
		{
			_nextRequestSuccess = success;
		}

		public override AbstractCommunicatorRequest CreateRequest(string messageType, IDictionary<string, object> data = null)
		{
			LastRequestType = messageType;
			LastRequestData = data;
			RequestCount++;
			// Return MockSnipeCommunicatorRequest which extends SnipeCommunicatorRequest
			// to handle the case where CreateRequest is called through base class reference
			return new MockSnipeCommunicatorRequest(_mockCommunicator, _mockCommunicator.Services, _mockAuth, messageType, data, _nextRequestSuccess);
		}
	}

	internal class MockSnipeCommunicatorRequest : SnipeCommunicatorRequest
	{
		private readonly bool _success;

		public MockSnipeCommunicatorRequest(ISnipeCommunicator communicator, ISnipeServices services, AuthSubsystem auth,
			string messageType, IDictionary<string, object> data, bool success)
			: base(communicator, services, auth, messageType, data)
		{
			_success = success;
		}

		protected override void OnCommunicatorReady()
		{
			// Override to immediately invoke callback without actual network call
			var response = new Dictionary<string, object>();
			InvokeCallback(_success ? "ok" : "error", response);
		}

		protected override void OnWillReconnect()
		{
			// Override to prevent null reference exceptions when _authSubsystem is null
			// For testing, we treat reconnection as ready and skip the reconnection logic
			OnCommunicatorReady();
		}

		protected override bool CheckCommunicatorValid()
		{
			return true; // Always valid for testing
		}

		protected override bool CheckCommunicatorReady()
		{
			return true; // Always ready for testing
		}
	}

	internal class MockSnipeApiUserAttributes : SnipeApiUserAttributes
	{
		public MockSnipeApiUserAttributes(AbstractSnipeApiService snipeApiService) : base(snipeApiService)
		{
		}
	}

	internal class MockSnipeApiReadOnlyUserAttribute<T> : SnipeApiReadOnlyUserAttribute<T>
	{
		public MockSnipeApiReadOnlyUserAttribute(AbstractSnipeApiService snipeApi, string key) : base(snipeApi, key)
		{
			// Don't set _initialized = true here - let it be set when SetValue is called
			// This allows testing uninitialized attributes
		}

		public void SetValue(T value)
		{
			// Set value and ensure initialized so ValueChanged events are raised
			var oldValue = GetValue();
			bool wasInitialized = _initialized;
			SetValue(value, null); // SetValue(TAttrValue val, SetCallback callback = null)
			// After SetValue, _initialized will be true, so ValueChanged will fire on subsequent calls
			// But for testing, we need to ensure the event fires even on the first call
			if (!wasInitialized)
			{
				RaiseValueChangedEvent(default(T), value);
			}
		}

		public void SetInitialized(bool initialized)
		{
			_initialized = initialized;
		}
	}

	// Helper to inject a delayed request mechanism for testing race conditions
	internal class DelayedMockSnipeApiService : MockSnipeApiService
	{
		public Action<string, IDictionary<string, object>> PendingCallback;
		public bool AutoComplete = true;

		public override AbstractCommunicatorRequest CreateRequest(string messageType, IDictionary<string, object> data = null)
		{
			if (!AutoComplete)
			{
				// Increment request count
				var req = base.CreateRequest(messageType, data);

				// Return our manual request, reusing the protected fields from base
				return new ManualCommunicatorRequest(this, _mockCommunicator, _mockAuth, messageType, data, _nextRequestSuccess);
			}
			return base.CreateRequest(messageType, data);
		}
	}

	internal class ManualCommunicatorRequest : MockSnipeCommunicatorRequest
	{
		private DelayedMockSnipeApiService _service;

		public ManualCommunicatorRequest(DelayedMockSnipeApiService service, ISnipeCommunicator communicator, AuthSubsystem auth, string messageType, IDictionary<string, object> data, bool success)
			: base(communicator, communicator.Services, auth, messageType, data, success)
		{
			_service = service;
		}

		protected override void OnCommunicatorReady()
		{
			// Intercept execution here.
			// Save the callback capability to the service so the test can call it.
			_service.PendingCallback = (errorCode, responseData) => {
				 // We need to call InvokeCallback on THIS instance.
				 this.InvokeCallback(errorCode, responseData);
			};
		}
	}

	internal sealed class TestObserver<T> : IObserver<T>
	{
		public int NextCount { get; private set; }
		public T LastValue { get; private set; }

		public void OnCompleted()
		{
		}

		public void OnError(Exception error)
		{
		}

		public void OnNext(T value)
		{
			NextCount++;
			LastValue = value;
		}
	}
}
