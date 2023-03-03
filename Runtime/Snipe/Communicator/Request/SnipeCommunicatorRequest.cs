﻿
namespace MiniIT.Snipe
{
	public class SnipeCommunicatorRequest : AbstractCommunicatorRequest
	{
		public bool WaitingForRoomJoined { get; private set; } = false;

		private AuthSubsystem _authSubsystem;
		
		public SnipeCommunicatorRequest(SnipeCommunicator communicator,
			AuthSubsystem authSubsystem,
			string messageType = null,
			SnipeObject data = null)
			: base(communicator, messageType, data)
		{
			_authSubsystem = authSubsystem;
		}

		protected override bool CheckCommunicatorValid()
		{
			return !(_communicator == null || _communicator.RoomJoined == false && MessageType == SnipeMessageTypes.ROOM_LEAVE);
		}

		protected override bool CheckCommunicatorReady()
		{
			return _communicator.LoggedIn;
		}

		protected override void OnCommunicatorReady()
		{
			if (_communicator.RoomJoined != true &&
				MessageType.StartsWith(SnipeMessageTypes.PREFIX_ROOM) &&
				MessageType != SnipeMessageTypes.ROOM_JOIN &&
				MessageType != SnipeMessageTypes.ROOM_LEAVE)
			{
				WaitingForRoomJoined = true;
			}
			
			_communicator.ConnectionFailed -= OnConnectionClosed;
			_communicator.ConnectionFailed += OnConnectionClosed;
			
			if ((_callback != null || WaitingForRoomJoined) && MessageType != SnipeMessageTypes.ROOM_LEAVE)
			{
				_waitingForResponse = true;
				_communicator.MessageReceived -= OnMessageReceived;
				_communicator.MessageReceived += OnMessageReceived;
			}
			
			if (!WaitingForRoomJoined)
			{
				DoSendRequest();
			}
		}
		
		protected override void OnConnectionClosed(bool will_retry = false)
		{
			if (will_retry)
			{
				_waitingForResponse = false;

				_communicator.ConnectionSucceeded -= OnCommunicatorReady;
				_authSubsystem.LoginSucceeded -= OnCommunicatorReady;
				_communicator.MessageReceived -= OnMessageReceived;
				
				if (_communicator.AllowRequestsToWaitForLogin)
				{
					DebugLogger.Log($"[SnipeCommunicatorRequest] Waiting for login - {MessageType} - {Data?.ToJSONString()}");

					_authSubsystem.LoginSucceeded += OnCommunicatorReady;
				}
				else
				{
					InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
				}
			}
			else
			{
				Dispose();
			}
		}

		protected override void OnMessageReceived(string message_type, string error_code, SnipeObject response_data, int request_id)
		{
			if (_communicator == null)
				return;
			
			if (WaitingForRoomJoined && _communicator.RoomJoined == true)
			{
				DebugLogger.Log($"[SnipeCommunicatorRequest] OnMessageReceived - Room joined. Send {MessageType}, id = {_requestId}");
				
				WaitingForRoomJoined = false;
				DoSendRequest();
				return;
			}
			
			base.OnMessageReceived(message_type, error_code, response_data, request_id);
		}

		public override void Dispose()
		{
			_authSubsystem.LoginSucceeded -= OnCommunicatorReady;
			base.Dispose();
		}
	}
}
