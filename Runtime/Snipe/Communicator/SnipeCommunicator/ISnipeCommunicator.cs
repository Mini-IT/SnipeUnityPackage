using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public delegate void MessageReceivedHandler(string messageType, string errorCode, IDictionary<string, object> data, int requestId);

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

		ISnipeServices Services { get; }
		TimeSpan CurrentRequestElapsed { get; }

		event Action ConnectionEstablished;
		event Action ConnectionClosed;
		event Action ConnectionDisrupted;
		event Action ReconnectionScheduled;
		event MessageReceivedHandler MessageReceived;
		event Action PreDestroy;

		void Reconfigure(SnipeOptions options);
		void Start();
		void Disconnect();
		void DisposeRoomRequests();
		void DisposeRequests();
		void SetIntensiveHeartbeat(bool value);

		int SendRequest(string messageType, IDictionary<string, object> data);
	}
}
