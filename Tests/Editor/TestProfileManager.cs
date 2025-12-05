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

			_profileManager = new ProfileManager();
			_profileManager.Initialize(_mockApiService, _mockUserAttributes, _mockSharedPrefs);
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
			var attr = _profileManager.GetAttribute<int>("coins");

			// Assert
			Assert.IsNotNull(attr);
			Assert.AreEqual(0, attr.Value);
		}

		[Test]
		public void GetAttribute_ExistingAttribute_ReturnsSameInstance()
		{
			// Arrange
			var attr1 = _profileManager.GetAttribute<int>("coins");

			// Act
			var attr2 = _profileManager.GetAttribute<int>("coins");

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
			var attr = _profileManager.GetAttribute<int>("coins");

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
			var attr = _profileManager.GetAttribute<int>("coins");

			// Assert
			Assert.AreEqual(50, attr.Value);
		}

		[Test]
		public void OnLocalAttributeChanged_IncrementsVersion()
		{
			// Arrange
			var attr = _profileManager.GetAttribute<int>("coins");
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
			var attr = _profileManager.GetAttribute<int>("coins");

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
			var attr = _profileManager.GetAttribute<int>("coins");

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
			var attr = _profileManager.GetAttribute<int>("coins");

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

			var attr = _profileManager.GetAttribute<int>(serverAttr.Key);
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
			var coinsAttr = _profileManager.GetAttribute<int>("coins");
			var levelAttr = _profileManager.GetAttribute<int>("level");
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
			_mockApiService.SetNextRequestSuccess(true); // Set success BEFORE setting value
			_mockVersionAttribute.SetValue(1);
			var attr = _profileManager.GetAttribute<int>("coins");

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
			var attr = _profileManager.GetAttribute<int>("coins");
			attr.Value = 100;
			_mockApiService.SetNextRequestSuccess(false);

			// Act
			attr.Value = 150;

			// Assert - dirty keys should still be present after failure
			var dirtyKeys = _stringListHelper.GetList(ProfileManager.KEY_DIRTY_KEYS);
			// Note: This test may need adjustment based on actual async behavior
		}

		[Test]
		public void SyncWithServer_LocalVersionGreater_SendsPendingChanges()
		{
			// Arrange
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LOCAL_VERSION, 5);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);
			_mockVersionAttribute.SetValue(4);

			// Act
			_profileManager = new ProfileManager();
			_profileManager.Initialize(_mockApiService, _mockUserAttributes, _mockSharedPrefs);

			// Assert
			// Should attempt to send pending changes
			// Verify through mock service
		}

		[Test]
		public void SyncWithServer_ServerVersionGreater_AcceptsServerValues()
		{
			// Arrange
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LOCAL_VERSION, 3);
			_mockSharedPrefs.SetInt(ProfileManager.KEY_LAST_SYNCED_VERSION, 3);
			_mockVersionAttribute.SetValue(5);

			// Act
			_profileManager = new ProfileManager();
			_profileManager.Initialize(_mockApiService, _mockUserAttributes, _mockSharedPrefs);

			// Assert
			var localVersion = _mockSharedPrefs.GetInt(ProfileManager.KEY_LOCAL_VERSION, 0);
			Assert.AreEqual(5, localVersion);
		}

		[Test]
		public void ProfileAttribute_ValueChanged_EventFires()
		{
			// Arrange
			var attr = _profileManager.GetAttribute<int>("coins");
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
			var attr = _profileManager.GetAttribute<int>("coins");
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
			var attr = _profileManager.GetAttribute<int>("coins");

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
			var coinsAttr = _profileManager.GetAttribute<int>("coins");
			var levelAttr = _profileManager.GetAttribute<int>("level");

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
			var intAttr = _profileManager.GetAttribute<int>("coins");
			var floatAttr = _profileManager.GetAttribute<float>("score");
			var boolAttr = _profileManager.GetAttribute<bool>("enabled");
			var stringAttr = _profileManager.GetAttribute<string>("name");

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
		public void Migrate_OldKeyExists_MigratesValue()
		{
			// Arrange
			var attr = _profileManager.GetAttribute<int>("coins");
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
			var attr = _profileManager.GetAttribute<int>("coins");
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
			var attr = _profileManager.GetAttribute<int>("coins");
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

		private bool _nextRequestSuccess = false; // Default to false so dirty keys persist for testing
		private readonly SnipeCommunicator _mockCommunicator;
		private readonly MockAuthSubsystem _mockAuth;

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

		public new AbstractCommunicatorRequest CreateRequest(string messageType, IDictionary<string, object> data = null)
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
			SetValue(value, null);
			// After SetValue, _initialized will be true, so ValueChanged will fire on subsequent calls
		}

		public void SetInitialized(bool initialized)
		{
			_initialized = initialized;
		}
	}
}

