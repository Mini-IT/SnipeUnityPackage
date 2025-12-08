using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	/// <summary>
	/// Delegate for message received events
	/// </summary>
	public delegate void MessageReceivedHandler(string messageType, string errorCode, IDictionary<string, object> data, int requestId);

	/// <summary>
	/// Interface for SnipeCommunicator to enable mocking for unit tests
	/// </summary>
	public interface ISnipeCommunicator : IDisposable
	{
		int InstanceId { get; }
		string ConnectionId { get; }
		bool AllowRequestsToWaitForLogin { get; set; }
		int RestoreConnectionAttempts { get; set; }
		List<AbstractCommunicatorRequest> Requests { get; }
		HashSet<SnipeRequestDescriptor> MergeableRequestTypes { get; }
		bool Connected { get; }
		bool LoggedIn { get; }
		bool? RoomJoined { get; }
		bool BatchMode { get; set; }
		TimeSpan CurrentRequestElapsed { get; }

		// Events
		event Action ConnectionEstablished;
		event Action ConnectionClosed;
		event Action ConnectionDisrupted;
		event Action ReconnectionScheduled;
		event MessageReceivedHandler MessageReceived;
		event Action PreDestroy;

		// Methods
		void Initialize(SnipeConfig config);
		void Start();
		void Disconnect();
		void DisposeRoomRequests();
		void DisposeRequests();
		void SetIntensiveHeartbeat(bool value);
	}

	internal interface IRawRequestSender
	{
		int SendRequest(string messageType, IDictionary<string, object> data);
	}
}
