using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using MiniIT.Snipe;
using MiniIT.Snipe.Api;
using MiniIT.Snipe.Unity;
using MiniIT.Storage;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestProfileManager
	{
		private ProfileManager _profileManager;
		private MockSnipeApiService _mockApiService;
		private MockSnipeApiUserAttributes _mockUserAttributes;
		private MockSnipeApiReadOnlyUserAttribute<int> _mockVersionAttribute;
		private MockSharedPrefs _mockSharedPrefs;
		private PlayerPrefsStringListHelper _stringListHelper;

		[SetUp]
		public void SetUp()
		{
			// Dispose existing SnipeServices if initialized
			if (SnipeServices.IsInitialized)
			{
				SnipeServices.Dispose();
			}

			// Create mock shared prefs
			_mockSharedPrefs = new MockSharedPrefs();
			_stringListHelper = new PlayerPrefsStringListHelper(_mockSharedPrefs);

			// Initialize SnipeServices with mock factory
			var mockFactory = new MockUnitySnipeServicesFactory(_mockSharedPrefs);
			SnipeServices.Initialize(mockFactory);

			_mockApiService = new MockSnipeApiService();
			_mockUserAttributes = new MockSnipeApiUserAttributes(_mockApiService);
			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "_version");
			// Initialize version attribute with 0 so ValueChanged events will be raised on subsequent SetValue calls
			_mockVersionAttribute.SetValue(0);
			_mockUserAttributes.RegisterAttribute(_mockVersionAttribute);

			_profileManager = new ProfileManager(_mockApiService, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
		}

		[TearDown]
		public void TearDown()
		{
			_profileManager?.Dispose();
			SnipeServices.Dispose();
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
		public void GetAttribute_ServerAttributeNotInitialized_UsesLocalStorage()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			// Not initialized - IsInitialized will be false
			_mockUserAttributes.RegisterAttribute(serverAttr);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 50);
			_mockSharedPrefs.Save();

			// Act
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Assert
			Assert.AreEqual(50, attr.Value);
		}

		[Test]
		public void OnLocalAttributeChanged_IncrementsVersion()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			var initialVersion = _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION, 0);

			// Act
			attr.Value = 100;

			// Assert
			var newVersion = _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION, 0);
			Assert.AreEqual(initialVersion + 1, newVersion);
		}

		[Test]
		public void OnLocalAttributeChanged_AddsToDirtyKeys()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Act
			attr.Value = 100;

			// Assert
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.Contains("coins", dirtyKeys);
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
			serverAttr.SetValue(200);

			// Assert
			Assert.AreEqual(200, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.AreEqual(200, attr.Value);
		}

		[Test]
		public void OnServerAttributeChanged_RemovesFromDirtyKeys()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			serverAttr.SetInitialized(true); // simulate that the value was already received from server

			var attr = _profileManager.GetAttribute<int>(serverAttr);
			attr.Value = 100; // Add to dirty keys, localVersion becomes 1

			// Set server version to be >= local version so the key is removed
			_mockVersionAttribute.SetValue(1);

			// Act
			serverAttr.SetValue(200);

			// Assert
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.IsFalse(dirtyKeys.Contains("coins"));
		}

		[Test]
		public void RebuildPendingChanges_WithDirtyKeys_ReturnsCorrectValues()
		{
			// Arrange
			var coinsServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			var levelServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "level");
			_mockUserAttributes.RegisterAttribute(coinsServerAttr);
			_mockUserAttributes.RegisterAttribute(levelServerAttr);
			var coinsAttr = _profileManager.GetAttribute<int>(coinsServerAttr);
			var levelAttr = _profileManager.GetAttribute<int>(levelServerAttr);
			coinsAttr.Value = 100;
			levelAttr.Value = 5;

			// Act
			// Access private method via reflection or make it internal for testing
			// For now, we'll test indirectly through SendPendingChanges behavior
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);

			// Assert
			Assert.Contains("coins", dirtyKeys);
			Assert.Contains("level", dirtyKeys);
			Assert.AreEqual(100, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.AreEqual(5, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "level", 0));
		}

		[Test]
		public void SendPendingChanges_Success_ClearsDirtyKeys()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetInitialized(true); // simulate that the value was already received from server
			_mockUserAttributes.RegisterAttribute(serverAttr);

			_mockApiService.SetNextRequestSuccess(true); // Set success BEFORE setting value
			_mockVersionAttribute.SetValue(1);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Act - setting value triggers SendPendingChanges automatically with success enabled
			attr.Value = 100;

			// Force flush SharedPrefs to ensure dirty keys are persisted/cleared
			_mockSharedPrefs.Save();

			// Assert - callback should have been invoked synchronously and cleared dirty keys
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.IsEmpty(dirtyKeys, "Dirty keys should be cleared after successful sync");
		}

		[Test]
		public void SendPendingChanges_Failure_KeepsDirtyKeys()
		{
			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			serverAttr.SetInitialized(true); // simulate that the value was already received from server
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			_mockApiService.SetNextRequestSuccess(false); // Set failure BEFORE setting value
			_mockVersionAttribute.SetValue(1);

			// Act
			attr.Value = 100;

			// Force flush SharedPrefs to ensure dirty keys are persisted
			_mockSharedPrefs.Save();

			// Assert - dirty keys should still be present after failure
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.Contains("coins", dirtyKeys, "Dirty keys should be kept after failed sync");
		}

		[Test]
		public void SyncWithServer_LocalVersionGreater_SendsPendingChangesSingle()
		{
			// Arrange - simulate a previous session where attribute was used and dirty keys were created
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			_mockVersionAttribute.SetValue(1);

			// Set up state as if from a previous session: local version > last synced version, with dirty keys
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LOCAL_VERSION, 5);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 100);
			_stringListHelper.Add(ProfileManager.KEY_DIRTY_KEYS, "coins");
			_mockSharedPrefs.Save();
			_mockVersionAttribute.SetValue(4);
			_mockApiService.SetNextRequestSuccess(true);
			var initialRequestCount = _mockApiService.RequestCount;

			// Act - create new ProfileManager, initialize, and retrieve attribute
			// Retrieving the attribute registers the local value getter, enabling RebuildPendingChanges to work
			_profileManager.Dispose();
			_profileManager = new ProfileManager(_mockApiService, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);
			// Get attribute to register local value getter so RebuildPendingChanges can find the value
			var attr = _profileManager.GetAttribute<int>(serverAttr);
			// Changing the value triggers SendPendingChanges, which will sync the existing dirty keys
			attr.Value = 150;

			// Assert - should attempt to send pending changes when local version > last synced version
			Assert.Greater(_mockApiService.RequestCount, initialRequestCount,
				"Request should be made when local version is greater than last synced version");
			Assert.AreEqual("attr.set", _mockApiService.LastRequestType,
				"Request type should be attr.set for syncing pending changes");
		}

		// [Test]
		// public void SyncWithServer_LocalVersionGreater_SendsPendingChangesMulti()
		// {
		// 	// Arrange - simulate a previous session where attribute was used and dirty keys were created
		// 	var serverAttrCoins = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
		// 	var serverAttrPoints = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "points");
		// 	_mockUserAttributes.RegisterAttribute(serverAttrCoins);
		// 	_mockUserAttributes.RegisterAttribute(serverAttrPoints);
		// 	_mockVersionAttribute.SetValue(1);
		//
		// 	// Set up state as if from a previous session: local version > last synced version, with dirty keys
		// 	_mockSharedPrefs.SetInt(ProfileManager.KEY_LOCAL_VERSION, 5);
		// 	_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);
		// 	_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 100);
		// 	_mockSharedPrefs.SetInt(ProfileManager.KEY_ATTR_PREFIX + "points", 200);
		// 	_stringListHelper.Add(ProfileManager.KEY_DIRTY_KEYS, "coins");
		// 	_stringListHelper.Add(ProfileManager.KEY_DIRTY_KEYS, "points");
		// 	_mockSharedPrefs.Save();
		// 	_mockVersionAttribute.SetValue(4);
		// 	_mockApiService.SetNextRequestSuccess(true);
		// 	var initialRequestCount = _mockApiService.RequestCount;
		//
		// 	// Act - create new ProfileManager, initialize, and retrieve attribute
		// 	// Retrieving the attribute registers the local value getter, enabling RebuildPendingChanges to work
		// 	_profileManager.Dispose();
		// 	_profileManager = new ProfileManager();
		// 	_profileManager.Initialize(_mockApiService, _mockUserAttributes, _mockSharedPrefs);
		// 	// Get attribute to register local value getter so RebuildPendingChanges can find the value
		// 	var attrCoins = _profileManager.GetAttribute<int>(serverAttrCoins);
		// 	var attrPoints = _profileManager.GetAttribute<int>(serverAttrPoints);
		// 	// Changing the value triggers SendPendingChanges, which will sync the existing dirty keys
		// 	attrCoins.Value = 150;
		// 	attrPoints.Value = 220;
		//
		// 	// Assert - should attempt to send pending changes when local version > last synced version
		// 	Assert.Greater(_mockApiService.RequestCount, initialRequestCount,
		// 		"Request should be made when local version is greater than last synced version");
		// 	Assert.AreEqual("attr.setMulti", _mockApiService.LastRequestType,
		// 		"Request type should be attr.setMulti for syncing pending changes");
		// }

	[Test]
	public void SyncWithServer_ServerVersionGreater_AcceptsServerValues()
	{
		// Arrange
		_mockSharedPrefs.SetInt(ProfileManager.KEY_LOCAL_VERSION, 3);
		_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);
		_mockVersionAttribute.SetValue(5);

		// Act
		_profileManager = new ProfileManager(_mockApiService, _mockSharedPrefs);
		_profileManager.Initialize(_mockVersionAttribute);

		// Assert
		var localVersion = _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION, 0);
		Assert.AreEqual(5, localVersion);
	}

	[Test]
	public void OfflineChanges_ReconnectWithOldServerValue_PreservesLocalChanges()
	{
		// This test reproduces the bug described in the review:
		// "коннектимся к снайпу получаем данные например у нас на сервере 2000 монет они начисляются.
		// выключаем инет зарабатываем 50 или тратим. включаем интернет востанавливается 2000.
		// тоесть все как раньше."

		// Arrange - simulate initial connection and getting server value
		var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
		serverAttr.SetValue(2000); // Initial server value
		_mockUserAttributes.RegisterAttribute(serverAttr);
		_mockVersionAttribute.SetValue(1);
		_mockApiService.SetNextRequestSuccess(false); // Simulate offline - requests will fail

		var attr = _profileManager.GetAttribute<int>(serverAttr);
		Assert.AreEqual(2000, attr.Value, "Initial value should be from server");

		// Act - User goes offline and earns/spends coins
		attr.Value = 2050; // Local change while offline (earned 50 coins)
		_mockSharedPrefs.Save();

		// Verify local change is stored and marked as dirty
		Assert.AreEqual(2050, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0),
			"Local storage should have offline changes");
		var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
		Assert.Contains("coins", dirtyKeys, "Coins should be in dirty keys after offline change");
		var localVersion = _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION, 0);
		Assert.Greater(localVersion, 0, "Local version should be incremented");

		// Simulate reconnection - server sends old value (hasn't received local changes yet)
		// Server version is still 1 (same as when we disconnected), but we have local changes
		_mockVersionAttribute.SetValue(1); // Server version hasn't changed
		serverAttr.SetValue(2000); // Server sends old value via ValueChanged event

		// Assert - Local offline changes should be preserved
		Assert.AreEqual(2050, attr.Value,
			"Local offline changes should be preserved when server sends old value");
		Assert.AreEqual(2050, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0),
			"Local storage should preserve offline changes");
		
		// Dirty keys should remain so the value can be synced
		dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
		Assert.Contains("coins", dirtyKeys,
			"Dirty keys should remain so local changes can be synced to server");
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
		_mockVersionAttribute.SetValue(5); // Server version is now newer
		serverAttr.SetValue(2100); // Server has different value with newer version

		// Assert - Server value should be accepted when server version is newer
		Assert.AreEqual(2100, attr.Value,
			"Server value should be accepted when server version is newer");
		Assert.AreEqual(2100, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0),
			"Local storage should be updated with server value");
		
		// Dirty keys should be removed since server version is newer
		var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
		Assert.IsFalse(dirtyKeys.Contains("coins"),
			"Dirty keys should be removed when server version is newer");
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
			// But OnLocalAttributeChanged should NOT be called
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.IsFalse(dirtyKeys.Contains("coins"));
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
			Assert.DoesNotThrow(() => serverAttr.SetValue(100));
		}

		[Test]
		public void MultipleAttributes_IndependentTracking()
		{
			// Arrange
			var coinsServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "coins");
			var levelServerAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "level");
			_mockUserAttributes.RegisterAttribute(coinsServerAttr);
			_mockUserAttributes.RegisterAttribute(levelServerAttr);
			var coinsAttr = _profileManager.GetAttribute<int>(coinsServerAttr);
			var levelAttr = _profileManager.GetAttribute<int>(levelServerAttr);

			// Act
			coinsAttr.Value = 100;
			levelAttr.Value = 5;

			// Assert
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.Contains("coins", dirtyKeys);
			Assert.Contains("level", dirtyKeys);
			Assert.AreEqual(100, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "coins", 0));
			Assert.AreEqual(5, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "level", 0));
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
			intListServerAttr.SetValue(serverList);

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
		public void RaceCondition_ChangeDuringSync_ResultIsClearedButNotSent()
		{
			// This test demonstrates a BUG where changes made during a sync are cleared from dirty keys
			// without being sent.

			// Setup with Delayed Service
			var delayedService = new DelayedMockSnipeApiService();
			delayedService.AutoComplete = false;

			// Re-init manager with delayed service
			_profileManager.Dispose();
			_mockUserAttributes = new MockSnipeApiUserAttributes(delayedService);
			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(delayedService, "_version");
			_mockUserAttributes.RegisterAttribute(_mockVersionAttribute);
			_profileManager = new ProfileManager(delayedService, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);

			var coinsAttr = CreateAttribute<int>("coins", delayedService);
			var gemsAttr = CreateAttribute<int>("gems", delayedService);

			// 1. Change Coins -> Starts Sync
			coinsAttr.Value = 100;

			// Verify sync started but not finished
			Assert.IsNotNull(delayedService.PendingCallback, "Sync should be in progress");

			// 2. Change Gems -> Should be added to dirty keys
			gemsAttr.Value = 50;

			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.Contains("coins", dirtyKeys);
			Assert.Contains("gems", dirtyKeys);

			// 3. Complete the first sync (which only contained "coins")
			delayedService.PendingCallback.Invoke("ok", new Dictionary<string, object>());

			// 4. Assert State
			dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);

			// BUG EXPECTATION: The dirty keys are cleared, so "gems" is lost!
			Assert.IsEmpty(dirtyKeys, "Bug Reproduction: Dirty keys cleared implies 'gems' change is lost");

			// Verify only one request was made (for coins)
			Assert.AreEqual(1, delayedService.RequestCount);
		}

		[Test]
		public void Migrate_DoesNotMarkAsDirty_Demonstration()
		{
			// This test demonstrates a BUG where Migrate updates local storage but doesn't mark it dirty

			// Arrange
			var serverAttr = new MockSnipeApiReadOnlyUserAttribute<int>(_mockApiService, "migrated_coins");
			_mockUserAttributes.RegisterAttribute(serverAttr);
			var attr = _profileManager.GetAttribute<int>(serverAttr);

			// Setup old key
			string oldKey = "old_migrated_coins";
			_mockSharedPrefs.SetInt(oldKey, 500);
			_mockSharedPrefs.Save();

			// Act
			attr.Migrate(oldKey);

			// Assert
			Assert.AreEqual(500, attr.Value);
			Assert.AreEqual(500, _mockSharedPrefs.GetInt(ProfileManager.KEY_ATTR_PREFIX + "migrated_coins", 0));

			// BUG EXPECTATION: Dirty keys is empty, so this value is never synced to server
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.IsEmpty(dirtyKeys, "Bug Reproduction: Migrate should mark key as dirty but doesn't");
		}

		[Test]
		public void SyncFailure_SubsequentChange_RetriesAll()
		{
			// Setup with Delayed Service
			var delayedService = new DelayedMockSnipeApiService();
			delayedService.AutoComplete = false;

			// Re-init
			_profileManager.Dispose();
			_mockUserAttributes = new MockSnipeApiUserAttributes(delayedService);
			_mockVersionAttribute = new MockSnipeApiReadOnlyUserAttribute<int>(delayedService, "_version");
			_mockUserAttributes.RegisterAttribute(_mockVersionAttribute);
			_profileManager = new ProfileManager(delayedService, _mockSharedPrefs);
			_profileManager.Initialize(_mockVersionAttribute);

			var coinsAttr = CreateAttribute<int>("coins", delayedService);

			// 1. Change Coins -> Starts Sync
			coinsAttr.Value = 100;

			// 2. Fail the sync
			delayedService.PendingCallback.Invoke("error", null);

			// Dirty keys should remain
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			Assert.Contains("coins", dirtyKeys);

			// 3. Change Coins again (or another value) -> Should trigger new sync
			delayedService.PendingCallback = null; // Reset
			delayedService.AutoComplete = true; // Let next one pass normally or check data

			// To check data, we can inspect the request in the mock if we expanded it,
			// but for now checking that a request is made is enough.
			coinsAttr.Value = 200;

			// Assert
			// Request count should be 2 (1 failed, 1 new)
			Assert.AreEqual(2, delayedService.RequestCount);
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
	}

	internal class MockSharedPrefs : ISharedPrefs
	{
		private readonly Dictionary<string, object> _storage = new Dictionary<string, object>();

		public bool HasKey(string key)
		{
			return _storage.ContainsKey(key);
		}

		public void DeleteKey(string key)
		{
			_storage.Remove(key);
		}

		public void DeleteAll()
		{
			_storage.Clear();
		}

		public void Save()
		{
			// No-op for in-memory storage
		}

		public bool GetBool(string key, bool defaultValue = false)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				if (value is bool boolValue)
				{
					return boolValue;
				}
				if (value is int intValue)
				{
					return intValue != 0;
				}
			}
			return defaultValue;
		}

		public float GetFloat(string key, float defaultValue = 0)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				if (value is float floatValue)
				{
					return floatValue;
				}
			}
			return defaultValue;
		}

		public int GetInt(string key, int defaultValue = 0)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				if (value is int intValue)
				{
					return intValue;
				}
			}
			return defaultValue;
		}

		public string GetString(string key, string defaultValue = null)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				return value?.ToString() ?? defaultValue;
			}
			return defaultValue;
		}

		public void SetBool(string key, bool value)
		{
			_storage[key] = value;
		}

		public void SetFloat(string key, float value)
		{
			_storage[key] = value;
		}

		public void SetInt(string key, int value)
		{
			_storage[key] = value;
		}

		public void SetString(string key, string value)
		{
			_storage[key] = value ?? string.Empty;
		}
	}

	internal class MockUnitySnipeServicesFactory : UnitySnipeServicesFactory
	{
		private readonly ISharedPrefs _sharedPrefs;

		public MockUnitySnipeServicesFactory(ISharedPrefs sharedPrefs)
		{
			_sharedPrefs = sharedPrefs;
		}

		public override ISharedPrefs CreateSharedPrefs() => _sharedPrefs;
	}

	internal class MockAuthSubsystem : AuthSubsystem
	{
		public MockAuthSubsystem(SnipeCommunicator communicator)
			: base(0, communicator, null)
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
		public int RequestCount { get; private set; }

		private static MockAuthSubsystem s_tempMockAuth;

		protected bool _nextRequestSuccess = false; // Default to false so dirty keys persist for testing
		protected readonly SnipeCommunicator _mockCommunicator;
		protected readonly MockAuthSubsystem _mockAuth;

		public MockSnipeApiService() : this(CreateMockCommunicator())
		{
		}

		private static SnipeCommunicator CreateMockCommunicator()
		{
			return new SnipeCommunicator(null);
		}

		private MockSnipeApiService(SnipeCommunicator communicator) : base(communicator, CreateMockAuth(communicator))
		{
			_mockCommunicator = _communicator;
			_mockAuth = s_tempMockAuth;
			s_tempMockAuth = null;
		}

		private static MockAuthSubsystem CreateMockAuth(SnipeCommunicator communicator)
		{
			s_tempMockAuth ??= new MockAuthSubsystem(communicator);
			return s_tempMockAuth;
		}

		public void SetNextRequestSuccess(bool success)
		{
			_nextRequestSuccess = success;
		}

		public override AbstractCommunicatorRequest CreateRequest(string messageType, IDictionary<string, object> data = null)
		{
			LastRequestType = messageType;
			RequestCount++;
			// Return MockSnipeCommunicatorRequest which extends SnipeCommunicatorRequest
			// to handle the case where CreateRequest is called through base class reference
			return new MockSnipeCommunicatorRequest(_mockCommunicator, _mockAuth, messageType, data, _nextRequestSuccess);
		}
	}

	internal class MockSnipeCommunicatorRequest : SnipeCommunicatorRequest
	{
		private readonly bool _success;

		public MockSnipeCommunicatorRequest(SnipeCommunicator communicator, AuthSubsystem auth,
			string messageType, IDictionary<string, object> data, bool success)
			: base(communicator, auth, messageType, data)
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

		public ManualCommunicatorRequest(DelayedMockSnipeApiService service, SnipeCommunicator communicator, AuthSubsystem auth, string messageType, IDictionary<string, object> data, bool success)
			: base(communicator, auth, messageType, data, success)
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
}

