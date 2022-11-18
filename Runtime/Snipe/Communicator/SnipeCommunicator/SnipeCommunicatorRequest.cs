using System;
using System.Threading.Tasks;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeCommunicatorRequest : IDisposable
	{
		private const int RETRIES_COUNT = 3;
		private const int RETRY_DELAY = 1000; // milliseconds
		
		private static readonly SnipeObject EMPTY_DATA = new SnipeObject();
		
		public string MessageType { get; private set; }
		public SnipeObject Data { get; set; }
		
		public bool WaitingForRoomJoined { get; private set; } = false;
		
		public delegate void ResponseHandler(string error_code, SnipeObject data);

		private SnipeCommunicator _communicator;
		private ResponseHandler _callback;

		private int _requestId;
		private int _retriesLeft = RETRIES_COUNT;
		
		private bool _sent = false;
		private bool _waitingForResponse = false;
		private bool _authorization = false;

		public SnipeCommunicatorRequest(SnipeCommunicator communicator, string message_type = null)
		{
			_communicator = communicator;
			MessageType = message_type;
			
			if (_communicator != null)
			{	
				_communicator.Requests.Add(this);
			}
		}

		public void Request(SnipeObject data, ResponseHandler callback = null)
		{
			Data = data;
			Request(callback);
		}

		public virtual void Request(ResponseHandler callback = null)
		{
			if (_sent)
				return;
				
			_callback = callback;
			SendRequest();
		}
		
		internal void RequestUnauthorized(SnipeObject data, ResponseHandler callback = null)
		{
			_authorization = true;
			Data = data;
			Request(callback);
		}
		
		private void SendRequest()
		{
			_sent = true;
			
			if (_communicator == null || _communicator.RoomJoined == false && MessageType == SnipeMessageTypes.ROOM_LEAVE)
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
				return;
			}
			
			if (string.IsNullOrEmpty(MessageType))
				MessageType = Data?.SafeGetString("t");

			if (string.IsNullOrEmpty(MessageType))
			{
				InvokeCallback(SnipeErrorCodes.INVALIND_DATA, EMPTY_DATA);
				return;
			}
			
			if (_communicator.LoggedIn || (_authorization && _communicator.Connected))
			{
				OnCommunicatorReady();
			}
			else
			{
				OnConnectionClosed(true);
			}
		}

		private void OnCommunicatorReady()
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
		
		private void DoSendRequest()
		{
			_requestId = 0;
			
			bool check_duplication = false;
			
			for (int i = 0; i < _communicator.MergeableRequestTypes.Count; i++)
			{
				SnipeRequestDescriptor descriptor = _communicator.MergeableRequestTypes[i];
				string mergeble_type = descriptor?.MessageType;
					
				if (mergeble_type != null && string.Equals(mergeble_type, this.MessageType, StringComparison.Ordinal))
				{
					bool matched = true;
						
					if (descriptor.Data != null && this.Data != null)
					{
						foreach (var pair in descriptor.Data)
						{
							if (this.Data[pair.Key] != pair.Value)
							{
								matched = false;
								break;
							}
						}
					}
						
					if (matched)
					{
						check_duplication = true;
						break;
					}
				}
			}
			
			if (check_duplication)
			{
				for (int i = 0; i < _communicator.Requests.Count; i++)
				{
					var request = _communicator.Requests[i];
					
					if (request == null)
						continue;
					
					if (request == this)
						break;
					
					if (request._authorization == this._authorization &&
						string.Equals(request.MessageType, this.MessageType, StringComparison.Ordinal) &&
						SnipeObject.ContentEquals(request.Data, this.Data))
					{
						_requestId = request._requestId;
						break;
					}
				}
			}
			
			if (_requestId != 0)
			{
				DebugLogger.Log($"[SnipeCommunicatorRequest] DoSendRequest - Same request found: {MessageType}, id = {_requestId}, mWaitingForResponse = {_waitingForResponse}");
				
				if (!_waitingForResponse)
				{
					Dispose();
				}
				return;
			}
			
			if (_communicator.LoggedIn || _authorization)
			{
				_requestId = _communicator.Client.SendRequest(this.MessageType, this.Data);
			}
			
			if (_requestId == 0)
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
			}
			
			if (!_waitingForResponse)
			{
				// keep this instance for a while to prevent duplicate requests
				DelayedDispose();
			}
		}

		private void OnConnectionClosed(bool will_retry = false)
		{
			if (will_retry)
			{
				_waitingForResponse = false;

				_communicator.ConnectionSucceeded -= OnCommunicatorReady;
				_communicator.LoginSucceeded -= OnCommunicatorReady;
				_communicator.MessageReceived -= OnMessageReceived;
				
				if (_authorization)
				{
					DebugLogger.Log($"[SnipeCommunicatorRequest] Waiting for connection - {MessageType}");
					
					_communicator.ConnectionSucceeded += OnCommunicatorReady;
				}
				else if (_communicator.AllowRequestsToWaitForLogin)
				{
					DebugLogger.Log($"[SnipeCommunicatorRequest] Waiting for login - {MessageType} - {Data?.ToJSONString()}");
					
					_communicator.LoginSucceeded += OnCommunicatorReady;
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

		private void OnMessageReceived(string message_type, string error_code, SnipeObject response_data, int request_id)
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
			
			if ((request_id == 0 || request_id == _requestId) && message_type == MessageType)
			{
				if (error_code == SnipeErrorCodes.SERVICE_OFFLINE && _retriesLeft > 0)
				{
					_retriesLeft--;
					DelayedRetryRequest();
					return;
				}

				InvokeCallback(error_code, response_data);
			}
		}
		
		private void InvokeCallback(string error_code, SnipeObject response_data)
		{
			var callback = _callback;
			
			Dispose();
			
			if (callback != null)
			{
				try
				{
					callback.Invoke(error_code, response_data);
				}
				catch (Exception e)
				{
					DebugLogger.Log($"[SnipeCommunicatorRequest] {MessageType} Callback invokation error: {e}");
				}
			}
		}
		
		private async void DelayedRetryRequest()
		{
			await Task.Delay(RETRY_DELAY);
			
			if (_communicator != null)
			{
				Request(_callback);
			}
		}
		
		private async void DelayedDispose()
		{
			await Task.Yield();
			Dispose();
		}

		public void Dispose()
		{
			if (_communicator != null)
			{
				if (_communicator.Requests != null)
				{
					_communicator.Requests.Remove(this);
				}
				
				_communicator.LoginSucceeded -= OnCommunicatorReady;
				_communicator.ConnectionSucceeded -= OnCommunicatorReady;
				_communicator.ConnectionFailed -= OnConnectionClosed;
				_communicator.MessageReceived -= OnMessageReceived;
				_communicator = null;
			}
			
			_callback = null;
			_waitingForResponse = false;
		}
	}
}