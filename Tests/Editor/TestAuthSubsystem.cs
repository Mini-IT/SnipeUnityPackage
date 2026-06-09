using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Configuration;
using NUnit.Framework;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestAuthSubsystem
	{
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

		private static Fixture CreateFixture(bool connected)
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

			var auth = new AuthSubsystemStub(0, options, communicator, NullAnalyticsContext.Instance, services);
			return new Fixture(auth, communicator);
		}

		private sealed class Fixture
		{
			public AuthSubsystemStub Auth { get; }
			public TestCommunicator Communicator { get; }

			public Fixture(AuthSubsystemStub auth, TestCommunicator communicator)
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
