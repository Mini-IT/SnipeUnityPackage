using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestAuthSubsystem
	{
		[Test]
		public void DroppedLogin_BeforeConnectionDisrupted_DoesNotCallDisconnect()
		{
			var fixture = StartReturningPlayer();
			fixture.Communicator.Connected = false;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.NOT_READY, null, 1);
			Assert.AreEqual(0, fixture.Communicator.DisconnectCalls);

			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.Start();

			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.USER_LOGIN, fixture.Communicator.SentRequests[1].MessageType);
		}

		[Test]
		public void RepeatedConnectionEstablished_DuringAndAfterLogin_SendsOneLogin()
		{
			var fixture = StartReturningPlayer();
			int successes = 0;
			fixture.Auth.LoginSucceeded += _ => successes++;
			fixture.Communicator.RaiseConnectionEstablished();
			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);

			fixture.Communicator.LoggedIn = true;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 1);
			fixture.Communicator.RaiseConnectionEstablished();
			fixture.Auth.Authorize();
			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(1, successes);

			fixture.Communicator.LoggedIn = false;
			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.RaiseConnectionEstablished();
			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.USER_LOGIN, fixture.Communicator.SentRequests[1].MessageType);
		}

		[UnityTest]
		public IEnumerator OldLoginRetry_DoesNotSendOnNewConnectionAwaitingLogin()
		{
			var fixture = StartReturningPlayer();
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.USER_ONLINE, null, 1);
			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.RaiseConnectionEstablished();
			yield return WaitForFirstRetry();

			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
		}

		[UnityTest]
		public IEnumerator LoginRetry_AfterSuccessOrDisposal_DoesNotSendAgain()
		{
			var succeeded = StartReturningPlayer();
			var disposed = StartReturningPlayer();
			succeeded.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.USER_ONLINE, null, 1);
			disposed.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.LOGOUT_IN_PROGRESS, null, 1);
			succeeded.Communicator.LoggedIn = true;
			succeeded.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 1);
			disposed.Auth.Dispose();
			yield return WaitForFirstRetry();

			Assert.AreEqual(1, succeeded.Communicator.SentRequests.Count);
			Assert.AreEqual(1, disposed.Communicator.SentRequests.Count);
		}

		[UnityTest]
		public IEnumerator LoginRetry_OnSameUnauthenticatedConnection_StillSends()
		{
			var fixture = StartReturningPlayer();
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.USER_ONLINE, null, 1);
			yield return WaitForFirstRetry();

			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.USER_LOGIN, fixture.Communicator.SentRequests[1].MessageType);
		}

		private static IEnumerator WaitForFirstRetry()
		{
			double end = Time.realtimeSinceStartupAsDouble + 1.3;
			while (Time.realtimeSinceStartupAsDouble < end)
			{
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator RegistrationProvider_FromOldConnection_CannotSendOnNewConnection()
		{
			var fixture = CreateFixture(true, useUnityAuth: true);
			var fetcher = new DeferredFetcher();
			fixture.Auth.RegisterBinding(new RegistrationBinding(fetcher, fixture.Communicator.Services));
			fixture.Communicator.RaiseConnectionEstablished();
			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.RaiseConnectionEstablished();
			Assert.AreEqual(2, fetcher.Callbacks.Count);

			fetcher.Callbacks.Dequeue()("old");
			yield return null;
			yield return null;
			Assert.AreEqual(0, fixture.Communicator.SentRequests.Count);

			fetcher.Callbacks.Dequeue()("current");
			for (int frame = 0; frame < 10 && fixture.Communicator.SentRequests.Count == 0; frame++)
			{
				yield return null;
			}

			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, fixture.Communicator.SentRequests[0].MessageType);
			fixture.Auth.Dispose();
		}

		private sealed class DeferredFetcher : AuthIdFetcher
		{
			public readonly Queue<Action<string>> Callbacks = new Queue<Action<string>>();

			public override void Fetch(bool waitInitialization, Action<string> callback = null)
			{
				Callbacks.Enqueue(callback);
			}
		}

		private sealed class RegistrationBinding : AuthBinding
		{
			public RegistrationBinding(AuthIdFetcher fetcher, ISnipeServices services) : base("test", fetcher, services)
			{
				AvailableForRegistration = true;
			}
		}

		private static Fixture StartReturningPlayer()
		{
			var fixture = CreateFixture(true);
			fixture.Communicator.RaiseConnectionEstablished();
			fixture.Communicator.LoggedIn = true;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 1);
			fixture.Communicator.LoggedIn = false;
			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.SentRequests.Clear();
			fixture.Communicator.RaiseConnectionEstablished();
			Assert.AreEqual(SnipeMessageTypes.USER_LOGIN, fixture.Communicator.SentRequests[0].MessageType);
			return fixture;
		}

		[Test]
		public void AutoLoginStarted_ManualAuthorize_DoesNotSendSecondLoginRequest()
		{
			var fixture = CreateFixture(true);

			fixture.Communicator.RaiseConnectionEstablished();
			fixture.Auth.Authorize();

			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, fixture.Communicator.SentRequests[0].MessageType);
		}

		[Test]
		public void ManualAuthorizeBeforeConnection_IsIgnored()
		{
			var fixture = CreateFixture(false);
			fixture.Auth.AutoLogin = false;

			fixture.Auth.Authorize();

			Assert.AreEqual(0, fixture.Communicator.SentRequests.Count);

			fixture.Communicator.Connected = true;
			fixture.Communicator.RaiseConnectionEstablished();

			Assert.AreEqual(0, fixture.Communicator.SentRequests.Count);
		}

		[Test]
		public void ManualAuthorizeTwiceDuringActiveLogin_SendsOneLoginRequest()
		{
			var fixture = CreateFixture(true);
			fixture.Auth.AutoLogin = false;
			fixture.Communicator.RaiseConnectionEstablished();

			fixture.Auth.Authorize();
			fixture.Auth.Authorize();

			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, fixture.Communicator.SentRequests[0].MessageType);
		}

		[Test]
		public void DisposeRequests_PendingRequest_InvokesCallbackWithNotReadyOnce()
		{
			var fixture = CreateFixture(true);
			fixture.Communicator.LoggedIn = true;

			int callbackCount = 0;
			string callbackErrorCode = null;
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) =>
			{
				callbackCount++;
				callbackErrorCode = errorCode;
			});

			fixture.Communicator.DisposeRequests();
			fixture.Communicator.DisposeRequests();

			Assert.AreEqual(1, callbackCount);
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, callbackErrorCode);
			Assert.AreEqual(0, fixture.Communicator.Requests.Count);
		}

		[Test]
		public void DisposeRequests_RequestWaitingForLogin_InvokesCallbackWithNotReadyOnce()
		{
			var fixture = CreateFixture(true);

			int callbackCount = 0;
			string callbackErrorCode = null;
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) =>
			{
				callbackCount++;
				callbackErrorCode = errorCode;
			});

			fixture.Communicator.DisposeRequests();
			fixture.Communicator.DisposeRequests();

			Assert.AreEqual(1, callbackCount);
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, callbackErrorCode);
			Assert.AreEqual(0, fixture.Communicator.Requests.Count);
		}

		[Test]
		public void ConnectionDisrupted_RequestWaitingForLogin_InvokesCallbackWithNotReadyOnce()
		{
			var fixture = CreateFixture(true);

			int callbackCount = 0;
			string callbackErrorCode = null;
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) =>
			{
				callbackCount++;
				callbackErrorCode = errorCode;
			});

			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.RaiseConnectionDisrupted();

			Assert.AreEqual(1, callbackCount);
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, callbackErrorCode);
			Assert.AreEqual(0, fixture.Communicator.Requests.Count);
		}

		[Test]
		public void RecoveredConnection_WaitingRequestSendsAndReceivesResponseOnce()
		{
			var fixture = StartReturningPlayer();
			int loginCount = 0;
			fixture.Auth.LoginSucceeded += _ => loginCount++;
			fixture.Communicator.LoggedIn = true;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 1);

			fixture.Communicator.Connected = false;
			fixture.Communicator.LoggedIn = false;
			var callbackErrors = new List<string>();
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) => callbackErrors.Add(errorCode));
			request.TryResumeAfterRecovery();
			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);

			fixture.Communicator.Connected = true;
			fixture.Communicator.LoggedIn = true;
			request.TryResumeAfterRecovery();
			request.TryResumeAfterRecovery();

			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_RESTORE, fixture.Communicator.SentRequests[1].MessageType);
			Assert.AreEqual(1, loginCount);
			Assert.IsEmpty(callbackErrors);

			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.AUTH_RESTORE, SnipeErrorCodes.OK, null, 2);
			CollectionAssert.AreEqual(new[] { SnipeErrorCodes.OK }, callbackErrors);
		}

		[Test]
		public void LostSession_LateRecoveryDoesNotSendDroppedRequestOrBypassNewLogin()
		{
			var fixture = StartReturningPlayer();
			fixture.Communicator.LoggedIn = true;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 1);

			fixture.Communicator.Connected = false;
			fixture.Communicator.LoggedIn = false;
			var callbackErrors = new List<string>();
			var dropped = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			dropped.Request((errorCode, _) => callbackErrors.Add(errorCode));
			fixture.Communicator.RaiseConnectionDisrupted();
			fixture.Communicator.Start();

			var pending = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			pending.Request();
			pending.TryResumeAfterRecovery();
			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.USER_LOGIN, fixture.Communicator.SentRequests[1].MessageType);

			fixture.Communicator.LoggedIn = true;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.USER_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 2);
			dropped.TryResumeAfterRecovery();
			pending.TryResumeAfterRecovery();

			CollectionAssert.AreEqual(new[] { SnipeErrorCodes.NOT_READY }, callbackErrors);
			Assert.AreEqual(3, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_RESTORE, fixture.Communicator.SentRequests[2].MessageType);
		}

		[Test]
		public void Authorize_RequestWaitingForLogin_WaitsAndSendsAfterLogin()
		{
			var fixture = CreateFixture(true);
			fixture.Auth.AutoLogin = false;
			fixture.Communicator.RaiseConnectionEstablished();

			int callbackCount = 0;
			string callbackErrorCode = null;
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) =>
			{
				callbackCount++;
				callbackErrorCode = errorCode;
			});

			fixture.Auth.Authorize();

			Assert.AreEqual(0, callbackCount);
			Assert.IsNull(callbackErrorCode);
			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, fixture.Communicator.SentRequests[0].MessageType);
			Assert.IsTrue(fixture.Communicator.Requests.Contains(request));
			Assert.IsFalse(fixture.Communicator.BatchMode);

			fixture.Communicator.LoggedIn = true;
			fixture.Communicator.RaiseMessageReceived(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, SnipeErrorCodes.OK, CreateRegisterAndLoginResponse(), 1);

			Assert.AreEqual(0, callbackCount);
			Assert.IsNull(callbackErrorCode);
			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_RESTORE, fixture.Communicator.SentRequests[1].MessageType);
		}

		[Test]
		public void Authorize_BatchModeRequestWaitingForLogin_InvokesCallbackWithNotReadyOnce()
		{
			var fixture = CreateFixture(true);
			fixture.Auth.AutoLogin = false;
			fixture.Communicator.BatchMode = true;

			int callbackCount = 0;
			string callbackErrorCode = null;
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) =>
			{
				callbackCount++;
				callbackErrorCode = errorCode;
			});

			fixture.Auth.Authorize();

			Assert.AreEqual(1, callbackCount);
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, callbackErrorCode);
			Assert.IsFalse(fixture.Communicator.Requests.Contains(request));
			Assert.AreEqual(1, fixture.Communicator.Requests.Count);
			Assert.AreEqual(1, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, fixture.Communicator.SentRequests[0].MessageType);
			Assert.IsFalse(fixture.Communicator.BatchMode);
		}

		[Test]
		public void Authorize_AllowRequestsToWaitForLoginFalse_DisposesAllRequests()
		{
			var fixture = CreateFixture(true);
			fixture.Auth.AutoLogin = false;
			fixture.Communicator.AllowRequestsToWaitForLogin = false;
			fixture.Communicator.LoggedIn = true;

			int callbackCount = 0;
			string callbackErrorCode = null;
			var request = new SnipeCommunicatorRequest(fixture.Communicator, fixture.Communicator.Services, fixture.Auth, SnipeMessageTypes.AUTH_RESTORE);
			request.Request((errorCode, _) =>
			{
				callbackCount++;
				callbackErrorCode = errorCode;
			});

			fixture.Communicator.LoggedIn = false;
			fixture.Auth.Authorize();

			Assert.AreEqual(1, callbackCount);
			Assert.AreEqual(SnipeErrorCodes.NOT_READY, callbackErrorCode);
			Assert.AreEqual(1, fixture.Communicator.Requests.Count);
			Assert.AreEqual(2, fixture.Communicator.SentRequests.Count);
			Assert.AreEqual(SnipeMessageTypes.AUTH_REGISTER_AND_LOGIN, fixture.Communicator.SentRequests[1].MessageType);
			Assert.IsFalse(fixture.Communicator.BatchMode);
		}

		private static IDictionary<string, object> CreateRegisterAndLoginResponse()
		{
			return new Dictionary<string, object>()
			{
				["id"] = 1,
				["uid"] = "uid",
				["password"] = "password",
				["authsBinded"] = new List<Dictionary<string, object>>(),
			};
		}

		private static Fixture CreateFixture(bool connected, bool useUnityAuth = false)
		{
			var services = new NullSnipeServices();
			var options = new SnipeOptions(0, new SnipeOptionsData()
			{
				ProjectInfo = new SnipeProjectInfo()
				{
					ProjectID = "test",
					ClientKey = "client",
					Mode = SnipeProjectMode.Dev,
				},
			}, services);

			var communicator = new TestCommunicator(services)
			{
				Connected = connected,
			};

			AuthSubsystem auth = useUnityAuth
				? new UnityAuthSubsystem(0, options, communicator, NullAnalyticsContext.Instance, services)
				: new AuthSubsystemStub(0, options, communicator, NullAnalyticsContext.Instance, services);
			return new Fixture(auth, communicator);
		}

		private sealed class Fixture
		{
			public AuthSubsystem Auth { get; }
			public TestCommunicator Communicator { get; }

			public Fixture(AuthSubsystem auth, TestCommunicator communicator)
			{
				Auth = auth;
				Communicator = communicator;
			}
		}

		private sealed class AuthSubsystemStub : AuthSubsystem
		{
			public AuthSubsystemStub(int contextId, SnipeOptions options, ISnipeCommunicator communicator, IAnalyticsContext analytics, ISnipeServices services)
				: base(contextId, options, communicator, analytics, services)
			{
			}

			protected override UniTaskVoid RegisterAndLogin()
			{
				RequestRegisterAndLogin(new Dictionary<string, Dictionary<string, object>>());
				return default;
			}
		}

		private sealed class TestCommunicator : ISnipeCommunicator
		{
			public int InstanceId { get; } = 1;
			public string ConnectionId { get; } = "connection";
			public bool AllowRequestsToWaitForLogin { get; set; } = true;
			public int RestoreConnectionAttempts { get; set; } = 3;
			public List<AbstractCommunicatorRequest> Requests { get; } = new List<AbstractCommunicatorRequest>();
			public HashSet<SnipeRequestDescriptor> MergeableRequestTypes { get; } = new HashSet<SnipeRequestDescriptor>();
			public bool Connected { get; set; }
			public bool LoggedIn { get; set; }
			public int DisconnectCalls { get; private set; }
			public bool? RoomJoined => null;
			public bool BatchMode { get; set; }
			public ISnipeServices Services { get; }
			public TimeSpan CurrentRequestElapsed => TimeSpan.Zero;
			public List<SentRequest> SentRequests { get; } = new List<SentRequest>();

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

			public void Reconfigure(SnipeOptions options) { }

			public void Start()
			{
				Connected = true;
				RaiseConnectionEstablished();
			}

			public void Disconnect()
			{
				DisconnectCalls++;
				Connected = false;
				ConnectionClosed?.Invoke();
			}

			public void DisposeRoomRequests() { }

			public void DisposeRequests()
			{
				var requests = new AbstractCommunicatorRequest[Requests.Count];
				Requests.CopyTo(requests);
				Requests.Clear();

				for (int i = 0; i < requests.Length; i++)
				{
					requests[i]?.DisposeWithCallback();
				}
			}

			public void SetIntensiveHeartbeat(bool value) { }

			public int SendRequest(string messageType, IDictionary<string, object> data)
			{
				SentRequests.Add(new SentRequest(messageType));
				return SentRequests.Count;
			}

			public void RaiseConnectionEstablished()
			{
				ConnectionEstablished?.Invoke();
			}

			public void RaiseMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
			{
				MessageReceived?.Invoke(messageType, errorCode, data, requestId);
			}

			public void RaiseConnectionDisrupted()
			{
				ConnectionDisrupted?.Invoke();
			}

			public void RaiseReconnectionScheduled()
			{
				ReconnectionScheduled?.Invoke();
			}

			public void Dispose()
			{
				PreDestroy?.Invoke();
				DisposeRequests();
			}
		}

		private sealed class SentRequest
		{
			public string MessageType { get; }

			public SentRequest(string messageType)
			{
				MessageType = messageType;
			}
		}
	}
}
