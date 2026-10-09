using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;
using MiniIT.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestAndroidRestoreCredentialsBinding
	{
		private const string CHALLENGE = "google.restoreKeyChallenge";
		private const string LOGIN = "google.restoreKeyLogin";
		private const string OPTIONS = "google.restoreKeyOptions";
		private const string REGISTER = "google.restoreKeyRegister";
		private const string READ_OPTIONS = "{\"rpId\":\"dev.snipe.dev\",\"challenge\":\"fresh-read-challenge\"}";
		private const string SAVE_OPTIONS = "{\"rp\":{\"id\":\"dev.snipe.dev\"},\"challenge\":\"fresh-save-challenge\"}";
		private const string ASSERTION = "{\"id\":\"restore-key\",\"response\":{\"signature\":\"opaque\"}}";
		private const string ATTESTATION = "{\"id\":\"new-key\",\"response\":{\"attestationObject\":\"opaque\"}}";
		private static readonly string[] ChallengeErrors = { "wrongClientKey", "projectBlocked", "internalVarsUnset" };
		private static readonly string[] LoginErrors = { "wrongClientKey", "projectBlocked", "paramsWrong", "wrongDeviceID", "internalVarsUnset", "noSuchChallenge" };
		private static readonly string[] InvalidCredentialErrors = { "noSuchRestoreKey", "authError" };
		private static readonly string[] OptionsErrors = { "notLoggedIn", "internalVarsUnset" };
		private static readonly string[] RegisterErrors = { "notLoggedIn", "paramsWrong", "internalVarsUnset", "noSuchChallenge", "authError" };
		private static readonly AndroidCredentialsStatus[] NativeFailures = { AndroidCredentialsStatus.Unsupported, AndroidCredentialsStatus.Error };
		private Fixture _fixture;

		[SetUp]
		public void SetUp()
		{
			_fixture = new Fixture();
		}

		[TearDown]
		public void TearDown()
		{
			_fixture.Dispose();
		}

		[UnityTest]
		public IEnumerator FirstLaunch_RegistersUnchangedAndroidResponseAfterLogin()
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.PrepareRegistration();
			Assert.AreEqual(READ_OPTIONS, _fixture.Binding.LastReadOptions);
			Assert.AreEqual(SAVE_OPTIONS, _fixture.Binding.LastSaveOptions);
			Assert.AreEqual(ATTESTATION, _fixture.Communicator.Pending(REGISTER).Data["response"]);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));

			_fixture.Communicator.Reply(REGISTER);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.OK, result.Result);
			Assert.AreEqual("3:10", File.ReadAllText(_fixture.MarkerPath));
			Assert.AreEqual(1, _fixture.Binding.ReadCount);
			Assert.AreEqual(1, _fixture.Binding.SaveCount);
			Assert.AreEqual(0, _fixture.Binding.DeleteCount);
		}

		[UnityTest]
		public IEnumerator RestoredCredential_ReloginsBeforeCreatingReplacementKey()
		{
			_fixture.Binding.ReadStatus = AndroidCredentialsStatus.Success;
			_fixture.LoginAs(10);
			yield return _fixture.PrepareRestoreLogin();
			var login = _fixture.Communicator.Pending(LOGIN);
			Assert.AreEqual("3device", login.Data["deviceID"]);
			Assert.AreEqual(ASSERTION, login.Data["response"]);
			Assert.AreEqual("client-XXXXXXXXXXXX", login.Data["ckey"]);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);

			_fixture.Communicator.Reply(LOGIN, SnipeErrorCodes.OK, new Dictionary<string, object>()
			{
				["id"] = 42,
				["deviceID"] = "3device",
			});
			yield return _fixture.WaitFor(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN);
			var auths = (IList<Dictionary<string, object>>)_fixture.Communicator.Pending(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN).Data["auths"];
			Assert.AreEqual("3device", auths[0]["login"]);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
			_fixture.CompleteLogin(42);
			var result = _fixture.Bind();
			yield return _fixture.WaitFor(OPTIONS);
			_fixture.Communicator.ReplyOptions(OPTIONS, SAVE_OPTIONS);
			yield return _fixture.WaitFor(REGISTER);
			_fixture.Communicator.Reply(REGISTER);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(42, _fixture.Auth.UserID);
			Assert.AreEqual("3:42", File.ReadAllText(_fixture.MarkerPath));
			Assert.AreEqual(1, _fixture.Binding.ReadCount);
			Assert.AreEqual(1, _fixture.Binding.SaveCount);
			Assert.AreEqual(1, _fixture.Communicator.DisconnectCount);
		}

		[UnityTest]
		public IEnumerator MatchingMarker_RepeatedLoginDoesNotCallGoogleOrNativeApi()
		{
			File.WriteAllText(_fixture.MarkerPath, "3:10");
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return result.AsUniTask().ToCoroutine();
			_fixture.LoginAs(10);
			yield return _fixture.Bind().AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.OK, result.Result);
			Assert.AreEqual(0, _fixture.Communicator.GoogleRequestCount);
			Assert.AreEqual(0, _fixture.Binding.ReadCount + _fixture.Binding.SaveCount + _fixture.Binding.DeleteCount);
		}

		[UnityTest]
		public IEnumerator DifferentAccount_DeletesOldKeyWithoutRestoringOldPlayer()
		{
			File.WriteAllText(_fixture.MarkerPath, "3:42");
			_fixture.Binding.ReadStatus = AndroidCredentialsStatus.Success;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.WaitFor(OPTIONS);
			_fixture.Communicator.ReplyOptions(OPTIONS, SAVE_OPTIONS);
			yield return _fixture.WaitFor(REGISTER);
			_fixture.Communicator.Reply(REGISTER);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(0, _fixture.Binding.ReadCount);
			Assert.AreEqual(1, _fixture.Binding.DeleteCount);
			Assert.AreEqual("3:10", File.ReadAllText(_fixture.MarkerPath));
			Assert.AreEqual(0, _fixture.Communicator.DisconnectCount);
		}

		[UnityTest]
		public IEnumerator RepeatedStartAndBind_ShareOneActiveOperation()
		{
			_fixture.LoginAs(10);
			var first = _fixture.Bind();
			_fixture.Binding.Start();
			_fixture.Auth.BindAll();
			var second = _fixture.Bind();
			yield return _fixture.PrepareRegistration();
			_fixture.Communicator.Reply(REGISTER);
			yield return first.AsUniTask().ToCoroutine();
			yield return second.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.OK, first.Result);
			Assert.AreEqual(SnipeErrorCodes.OK, second.Result);
			Assert.AreEqual(3, _fixture.Communicator.GoogleRequestCount);
			Assert.AreEqual(1, _fixture.Binding.SaveCount);
		}

		[UnityTest]
		public IEnumerator ChallengeError_PreservesCurrentAccount([ValueSource(nameof(ChallengeErrors))] string errorCode)
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.Reply(CHALLENGE, errorCode);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(errorCode, result.Result);
			Assert.AreEqual(10, _fixture.Auth.UserID);
			Assert.AreEqual(0, _fixture.Binding.ReadCount + _fixture.Binding.SaveCount + _fixture.Binding.DeleteCount);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
		}

		[UnityTest]
		public IEnumerator LoginError_DoesNotReplaceCredential([ValueSource(nameof(LoginErrors))] string errorCode)
		{
			_fixture.Binding.ReadStatus = AndroidCredentialsStatus.Success;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.PrepareRestoreLogin();
			_fixture.Communicator.Reply(LOGIN, errorCode);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(errorCode, result.Result);
			Assert.AreEqual(10, _fixture.Auth.UserID);
			Assert.AreEqual(0, _fixture.Binding.SaveCount + _fixture.Binding.DeleteCount);
			Assert.AreEqual(0, _fixture.Communicator.DisconnectCount);
		}

		[UnityTest]
		public IEnumerator InvalidRestoreKey_IsDeletedBeforeRegisteringCurrentPlayer([ValueSource(nameof(InvalidCredentialErrors))] string errorCode)
		{
			_fixture.Binding.ReadStatus = AndroidCredentialsStatus.Success;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.PrepareRestoreLogin();
			_fixture.Communicator.Reply(LOGIN, errorCode);
			yield return _fixture.WaitFor(OPTIONS);
			Assert.AreEqual(1, _fixture.Binding.DeleteCount);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
			_fixture.Communicator.ReplyOptions(OPTIONS, SAVE_OPTIONS);
			yield return _fixture.WaitFor(REGISTER);
			_fixture.Communicator.Reply(REGISTER);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual("3:10", File.ReadAllText(_fixture.MarkerPath));
			Assert.AreEqual(0, _fixture.Communicator.DisconnectCount);
		}

		[UnityTest]
		public IEnumerator OptionsError_DoesNotCreateKey([ValueSource(nameof(OptionsErrors))] string errorCode)
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return _fixture.WaitFor(OPTIONS);
			_fixture.Communicator.Reply(OPTIONS, errorCode);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(errorCode, result.Result);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
		}

		[UnityTest]
		public IEnumerator RegisterError_DoesNotConfirmMarker([ValueSource(nameof(RegisterErrors))] string errorCode)
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.PrepareRegistration();
			_fixture.Communicator.Reply(REGISTER, errorCode);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(errorCode, result.Result);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
			Assert.AreEqual(1, _fixture.Binding.SaveCount);
		}

		[UnityTest]
		public IEnumerator NativeReadFailure_DoesNotReplaceKey([ValueSource(nameof(NativeFailures))] AndroidCredentialsStatus status)
		{
			_fixture.Binding.ReadStatus = status;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.AreEqual(0, _fixture.Binding.SaveCount + _fixture.Binding.DeleteCount);
			Assert.AreEqual(1, _fixture.Communicator.GoogleRequestCount);
		}

		[UnityTest]
		public IEnumerator NativeSaveFailure_DoesNotRegisterKey([ValueSource(nameof(NativeFailures))] AndroidCredentialsStatus status)
		{
			_fixture.Binding.SaveStatus = status;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return _fixture.WaitFor(OPTIONS);
			_fixture.Communicator.ReplyOptions(OPTIONS, SAVE_OPTIONS);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
			Assert.AreEqual(2, _fixture.Communicator.GoogleRequestCount);
		}

		[UnityTest]
		public IEnumerator NativeDeleteFailure_DoesNotCreateReplacement([ValueSource(nameof(NativeFailures))] AndroidCredentialsStatus status)
		{
			File.WriteAllText(_fixture.MarkerPath, "3:42");
			_fixture.Binding.DeleteStatus = status;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.AreEqual(0, _fixture.Binding.ReadCount + _fixture.Binding.SaveCount);
			Assert.AreEqual(0, _fixture.Communicator.GoogleRequestCount);
			Assert.AreEqual("3:42", File.ReadAllText(_fixture.MarkerPath));
		}

		[UnityTest]
		public IEnumerator CancelledDeletion_RemovesUncertainMarkerWithoutCreatingKey()
		{
			File.WriteAllText(_fixture.MarkerPath, "3:42");
			var delete = new TaskCompletionSource<AndroidCredentialsStatus>();
			_fixture.Binding.DeleteTask = delete.Task;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.Disrupt();
			delete.SetResult(AndroidCredentialsStatus.Success);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
			Assert.AreEqual(0, _fixture.Communicator.GoogleRequestCount);
		}

		[UnityTest]
		public IEnumerator EmptyChallengeOptions_DoesNotCallNativeApi()
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, "");
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.INVALID_DATA, result.Result);
			Assert.AreEqual(0, _fixture.Binding.ReadCount);
		}

		[UnityTest]
		public IEnumerator EmptyCreationOptions_DoesNotSaveKey()
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return _fixture.WaitFor(OPTIONS);
			_fixture.Communicator.ReplyOptions(OPTIONS, "");
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.INVALID_DATA, result.Result);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
		}

		[UnityTest]
		public IEnumerator MissingDeviceBinding_DoesNotStartRestore()
		{
			_fixture.Dispose();
			_fixture = new Fixture(false);
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.AreEqual(0, _fixture.Communicator.GoogleRequestCount);
		}

		[UnityTest]
		public IEnumerator EmptyDeviceId_DoesNotSendRestoreLogin()
		{
			_fixture.Fetcher.Id = "";
			_fixture.Binding.ReadStatus = AndroidCredentialsStatus.Success;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.INVALID_DATA, result.Result);
			Assert.AreEqual(1, _fixture.Communicator.GoogleRequestCount);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
		}

		[UnityTest]
		public IEnumerator UnsupportedPlatform_DoesNotSendRequests()
		{
			_fixture.Binding.Supported = false;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(0, _fixture.Communicator.GoogleRequestCount);
			Assert.AreEqual(0, _fixture.Binding.ReadCount + _fixture.Binding.SaveCount + _fixture.Binding.DeleteCount);
		}

		[UnityTest]
		public IEnumerator RestoreLoginTimeout_DoesNotSaveTemporaryAccountKey()
		{
			_fixture.Binding.ReadStatus = AndroidCredentialsStatus.Success;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.PrepareRestoreLogin();
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.AreEqual(0, _fixture.Binding.SaveCount + _fixture.Binding.DeleteCount);
			Assert.AreEqual(0, _fixture.Communicator.Requests.Count);
			_fixture.Communicator.Reply(LOGIN, SnipeErrorCodes.OK, new Dictionary<string, object>()
			{
				["id"] = 42,
				["deviceID"] = "3device",
			});
			yield return null;
			Assert.AreEqual(10, _fixture.Auth.UserID);
			Assert.AreEqual(0, _fixture.Communicator.DisconnectCount);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
		}

		[UnityTest]
		public IEnumerator Disconnect_CancelsRequestAndIgnoresLateResponse()
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.Disrupt();
			yield return result.AsUniTask().ToCoroutine();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return null;
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.AreEqual(0, _fixture.Binding.ReadCount);
			Assert.AreEqual(0, _fixture.Communicator.Requests.Count);
		}

		[UnityTest]
		public IEnumerator DisposeViaAuthManager_CancelsNativeReadAndIgnoresLateResult()
		{
			var read = new TaskCompletionSource<(AndroidCredentialsStatus, string)>();
			_fixture.Binding.ReadTask = read.Task;
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			while (_fixture.Binding.ReadCount == 0)
			{
				yield return null;
			}
			_fixture.Auth.Dispose();
			Assert.IsTrue(_fixture.Binding.ReadToken.IsCancellationRequested);
			read.SetResult((AndroidCredentialsStatus.Success, ASSERTION));
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
			Assert.AreEqual(1, _fixture.Communicator.GoogleRequestCount);
		}

		[UnityTest]
		public IEnumerator AccountChangedBeforeRegisterReply_DoesNotWriteOldMarker()
		{
			_fixture.LoginAs(10);
			var result = _fixture.Bind();
			yield return _fixture.PrepareRegistration();
			_fixture.LoginAs(42);
			_fixture.Communicator.Reply(REGISTER);
			yield return result.AsUniTask().ToCoroutine();
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, result.Result);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
			Assert.AreEqual(42, _fixture.Auth.UserID);
		}

		[UnityTest]
		public IEnumerator Clear_RemovesMarkerAndDeletesNativeKeyOnce()
		{
			File.WriteAllText(_fixture.MarkerPath, "3:10");
			_fixture.LoginAs(10);
			var result = _fixture.Binding.ClearAsync();
			yield return result.AsUniTask().ToCoroutine();
			Assert.IsTrue(result.Result);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
			Assert.AreEqual(1, _fixture.Binding.DeleteCount);
			Assert.AreEqual(0, _fixture.Binding.SaveCount);
		}

		[UnityTest]
		public IEnumerator ClearDuringSave_WaitsForOldOperationAndDoesNotRegisterLateSave()
		{
			var save = new TaskCompletionSource<(AndroidCredentialsStatus, string)>();
			_fixture.Binding.SaveTask = save.Task;
			_fixture.LoginAs(10);
			_fixture.Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
			yield return _fixture.WaitFor(OPTIONS);
			_fixture.Communicator.ReplyOptions(OPTIONS, SAVE_OPTIONS);
			while (_fixture.Binding.SaveCount == 0)
			{
				yield return null;
			}
			var result = _fixture.Binding.ClearAsync();
			Assert.IsTrue(_fixture.Binding.SaveToken.IsCancellationRequested);
			Assert.IsFalse(result.IsCompleted);
			Assert.AreEqual(0, _fixture.Binding.DeleteCount);
			save.SetResult((AndroidCredentialsStatus.Success, ATTESTATION));
			yield return result.AsUniTask().ToCoroutine();
			Assert.IsTrue(result.Result);
			Assert.AreEqual(1, _fixture.Binding.DeleteCount);
			Assert.AreEqual(2, _fixture.Communicator.GoogleRequestCount);
			Assert.IsFalse(File.Exists(_fixture.MarkerPath));
		}

		private sealed class Fixture : IDisposable
		{
			public readonly string MarkerPath;
			public readonly TestCommunicator Communicator;
			public readonly AuthSubsystemStub Auth;
			public readonly TestBinding Binding;
			public readonly StubFetcher Fetcher = new StubFetcher();
			private readonly string _directory;
			private bool _disposed;

			public Fixture(bool withDeviceBinding = true)
			{
				_directory = Path.Combine(Path.GetTempPath(), "snipe-restore-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(_directory);
				MarkerPath = Path.Combine(_directory, "snipe-restore-key.txt");
				var services = new NullSnipeServices();
				var options = new SnipeOptions(3, new SnipeOptionsData()
				{
					ProjectInfo = new SnipeProjectInfo()
					{
						ProjectID = "test",
						ClientKey = "client-XXXXXXXXXXXX",
						Mode = SnipeProjectMode.Dev,
					},
				}, services);
				Communicator = new TestCommunicator(services);
				AuthBinding deviceBinding = withDeviceBinding ? new AuthBinding("dvid", Fetcher, services) : null;
				Auth = new AuthSubsystemStub(options, Communicator, services, deviceBinding);
				if (deviceBinding != null)
				{
					Auth.RegisterBinding(deviceBinding);
				}
				Binding = Auth.RegisterBinding(new TestBinding(services, deviceBinding, MarkerPath));
			}

			public void LoginAs(int userId)
			{
				Communicator.LoggedIn = false;
				Communicator.Start();
				CompleteLogin(userId);
			}

			public void CompleteLogin(int userId)
			{
				Communicator.LoggedIn = true;
				string type = Communicator.Pending(SnipeMessageTypes.USER_LOGIN) != null
					? SnipeMessageTypes.USER_LOGIN : SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN;
				Communicator.Reply(type, SnipeErrorCodes.OK, new Dictionary<string, object>()
				{
					["id"] = userId,
					["uid"] = "internal-uid",
					["password"] = "internal-password",
					["registrationDone"] = false,
					["authsBinded"] = new List<Dictionary<string, object>>()
					{
						new Dictionary<string, object>() { ["provider"] = "dvid" },
					},
				});
			}

			public Task<string> Bind()
			{
				var result = new TaskCompletionSource<string>();
				Binding.Bind((_, errorCode) => result.TrySetResult(errorCode));
				return result.Task;
			}

			public IEnumerator WaitFor(string messageType)
			{
				var deadline = DateTime.UtcNow.AddSeconds(5);
				while (Communicator.Pending(messageType) == null && DateTime.UtcNow < deadline)
				{
					yield return null;
				}
				Assert.IsNotNull(Communicator.Pending(messageType), "Expected request: " + messageType);
			}

			public IEnumerator PrepareRegistration()
			{
				Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
				yield return WaitFor(OPTIONS);
				Communicator.ReplyOptions(OPTIONS, SAVE_OPTIONS);
				yield return WaitFor(REGISTER);
			}

			public IEnumerator PrepareRestoreLogin()
			{
				Communicator.ReplyOptions(CHALLENGE, READ_OPTIONS);
				yield return WaitFor(LOGIN);
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				Auth.Dispose();
				Communicator.Dispose();
				Directory.Delete(_directory, true);
			}
		}

		private sealed class TestBinding : AndroidRestoreCredentialsBinding
		{
			private readonly string _path;
			public bool Supported = true;
			public AndroidCredentialsStatus ReadStatus = AndroidCredentialsStatus.NotFound;
			public AndroidCredentialsStatus SaveStatus = AndroidCredentialsStatus.Success;
			public AndroidCredentialsStatus DeleteStatus = AndroidCredentialsStatus.Success;
			public Task<(AndroidCredentialsStatus, string)> ReadTask;
			public Task<(AndroidCredentialsStatus, string)> SaveTask;
			public Task<AndroidCredentialsStatus> DeleteTask;
			public int ReadCount;
			public int SaveCount;
			public int DeleteCount;
			public string LastReadOptions;
			public string LastSaveOptions;
			public CancellationToken ReadToken;
			public CancellationToken SaveToken;
			protected override bool IsSupported => Supported;

			public TestBinding(ISnipeServices services, AuthBinding deviceBinding, string path)
				: base(services, deviceBinding)
			{
				_path = path;
			}

			protected override string GetRegistrationMarkerPath()
			{
				return _path;
			}

			protected override Task<(AndroidCredentialsStatus Status, string Json)> ReadCredentialAsync(string options, CancellationToken token)
			{
				ReadCount++;
				ReadToken = token;
				LastReadOptions = options;
				return ReadTask ?? Task.FromResult((ReadStatus, ASSERTION));
			}

			protected override Task<(AndroidCredentialsStatus Status, string Json)> SaveCredentialAsync(string options, CancellationToken token)
			{
				SaveCount++;
				SaveToken = token;
				LastSaveOptions = options;
				return SaveTask ?? Task.FromResult((SaveStatus, ATTESTATION));
			}

			protected override Task<AndroidCredentialsStatus> DeleteCredentialAsync(CancellationToken token)
			{
				DeleteCount++;
				return DeleteTask ?? Task.FromResult(DeleteStatus);
			}
		}

		private sealed class StubFetcher : AuthIdFetcher
		{
			public string Id = "device";

			public override void Fetch(bool waitInitialization, Action<string> callback = null)
			{
				Value = Id;
				callback?.Invoke(Value);
			}
		}

		private sealed class AuthSubsystemStub : AuthSubsystem
		{
			private readonly AuthBinding _deviceBinding;

			public AuthSubsystemStub(SnipeOptions options, ISnipeCommunicator communicator, ISnipeServices services, AuthBinding deviceBinding)
				: base(3, options, communicator, NullAnalyticsContext.Instance, services)
			{
				_deviceBinding = deviceBinding;
			}

			protected override UniTaskVoid RegisterAndLogin()
			{
				var providers = new Dictionary<string, Dictionary<string, object>>();
				if (_deviceBinding != null)
				{
					_deviceBinding.Fetcher.Fetch(false);
					providers["dvid"] = new Dictionary<string, object>()
					{
						["provider"] = "dvid",
						["login"] = _deviceBinding.GetUserId(),
					};
				}
				RequestRegisterAndLogin(providers);
				return default;
			}
		}

		private sealed class TestCommunicator : ISnipeCommunicator
		{
			public int InstanceId => 3;
			public string ConnectionId => "test-connection";
			public bool AllowRequestsToWaitForLogin { get; set; } = true;
			public int RestoreConnectionAttempts { get; set; } = 3;
			public List<AbstractCommunicatorRequest> Requests { get; } = new List<AbstractCommunicatorRequest>();
			public HashSet<SnipeRequestDescriptor> MergeableRequestTypes { get; } = new HashSet<SnipeRequestDescriptor>();
			public bool Connected { get; private set; }
			public bool LoggedIn { get; set; }
			public bool? RoomJoined => null;
			public bool BatchMode { get; set; }
			public ISnipeServices Services { get; }
			public TimeSpan CurrentRequestElapsed => TimeSpan.Zero;
			public int DisconnectCount;
			public int GoogleRequestCount;
			private readonly List<SentRequest> _sentRequests = new List<SentRequest>();
			private bool _disposed;

			public event Action ConnectionEstablished;
			public event Action ConnectionClosed;
			public event Action ConnectionDisrupted;
			public event Action ReconnectionScheduled;
			public event MessageReceivedHandler MessageReceived;
			public event Action PreDestroy;

			public TestCommunicator(ISnipeServices services)
			{
				Services = services;
			}

			public int SendRequest(string messageType, IDictionary<string, object> data)
			{
				var request = new SentRequest(messageType, data, _sentRequests.Count + 1);
				_sentRequests.Add(request);
				if (messageType.StartsWith("google.", StringComparison.Ordinal))
				{
					GoogleRequestCount++;
				}
				return request.Id;
			}

			public SentRequest Pending(string type)
			{
				for (int i = 0; i < _sentRequests.Count; i++)
				{
					var request = _sentRequests[i];
					if (!request.Replied && request.Type == type)
					{
						return request;
					}
				}
				return null;
			}

			public void Reply(string type, string errorCode = SnipeErrorCodes.OK, IDictionary<string, object> data = null)
			{
				var request = Pending(type);
				Assert.IsNotNull(request, "No pending request: " + type);
				request.Replied = true;
				MessageReceived?.Invoke(type, errorCode, data ?? new Dictionary<string, object>(), request.Id);
			}

			public void ReplyOptions(string type, string options)
			{
				Reply(type, SnipeErrorCodes.OK, new Dictionary<string, object>() { ["options"] = options });
			}

			public void Start()
			{
				if (!_disposed)
				{
					Connected = true;
					ConnectionEstablished?.Invoke();
				}
			}

			public void Disconnect()
			{
				DisconnectCount++;
				Connected = false;
				LoggedIn = false;
				ConnectionClosed?.Invoke();
			}

			public void Disrupt()
			{
				Connected = false;
				LoggedIn = false;
				ConnectionDisrupted?.Invoke();
			}

			public void Reconfigure(SnipeOptions options)
			{
			}

			public void DisposeRoomRequests()
			{
			}

			public void DisposeRequests()
			{
				while (Requests.Count > 0)
				{
					Requests[Requests.Count - 1].Dispose();
				}
			}

			public void SetIntensiveHeartbeat(bool value)
			{
			}

			public void Dispose()
			{
				_disposed = true;
				PreDestroy?.Invoke();
				DisposeRequests();
			}
		}

		private sealed class SentRequest
		{
			public readonly string Type;
			public readonly IDictionary<string, object> Data;
			public readonly int Id;
			public bool Replied;

			public SentRequest(string type, IDictionary<string, object> data, int id)
			{
				Type = type;
				Data = data;
				Id = id;
			}
		}
	}
}
