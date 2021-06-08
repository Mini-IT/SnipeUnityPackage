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
		
		public bool Active { get; private set; } = true;
		public string MessageType { get; private set; }
		public SnipeObject Data { get; set; }
		
		public bool WaitingForRoomJoined { get; private set; } = false;
		
		public delegate void ResponseHandler(string error_code, SnipeObject data);

		private SnipeCommunicator mCommunicator;
		private ResponseHandler mCallback;

		private int mRequestId;
		private int mRetriesLeft = RETRIES_COUNT;
		
		private bool mSent = false;
		private bool mWaitingForResponse = false;
		private bool mAuthorization = false;

		public SnipeCommunicatorRequest(SnipeCommunicator communicator, string message_type = null)
		{
			mCommunicator = communicator;
			MessageType = message_type;
			
			if (mCommunicator != null)
			{	
				mCommunicator.Requests.Add(this);
			}
		}

		public void Request(SnipeObject data, ResponseHandler callback = null)
		{
			Data = data;
			Request(callback);
		}

		public virtual void Request(ResponseHandler callback = null)
		{
			if (mSent)
				return;
				
			mCallback = callback;
			SendRequest();
		}
		
		internal void RequestAuth(SnipeObject data, ResponseHandler callback = null)
		{
			mAuthorization = true;
			Data = data;
			Request(callback);
		}
		
		internal void ResendInactive()
		{
			if (!Active)
			{
				Active = true;
				mSent = false;
				SendRequest();
			}
		}
		
		private void SendRequest()
		{
			mSent = true;
			
			if (mCommunicator == null || mCommunicator.RoomJoined == false && MessageType == SnipeMessageTypes.ROOM_LEAVE)
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
			
			if (mCommunicator.LoggedIn || mAuthorization)
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
			if (mCommunicator.RoomJoined != true &&
				MessageType.StartsWith(SnipeMessageTypes.PREFIX_ROOM) &&
				MessageType != SnipeMessageTypes.ROOM_JOIN &&
				MessageType != SnipeMessageTypes.ROOM_LEAVE)
			{
				WaitingForRoomJoined = true;
			}
			
			mCommunicator.ConnectionFailed -= OnConnectionClosed;
			mCommunicator.ConnectionFailed += OnConnectionClosed;
			
			if (mCallback != null || WaitingForRoomJoined)
			{
				mWaitingForResponse = true;
				mCommunicator.MessageReceived -= OnMessageReceived;
				mCommunicator.MessageReceived += OnMessageReceived;
			}
			
			if (!WaitingForRoomJoined)
			{
				DoSendRequest();
			}
		}
		
		private void DoSendRequest()
		{
			//mRequestId = mCommunicator.Request(this);
			if (mCommunicator.LoggedIn || mAuthorization)
				mRequestId = mCommunicator.Client.SendRequest(this.MessageType, this.Data);
			else
				mRequestId = 0;
			
			if (mRequestId == 0)
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
			}
			else if (!mWaitingForResponse)
			{
				Dispose();
			}
		}

		private void OnConnectionClosed(bool will_rety = false)
		{
			if (will_rety)
			{
				mWaitingForResponse = false;
				mCommunicator.MessageReceived -= OnMessageReceived;
				
				if (mCommunicator.AllowRequestsToWaitForLogin && !mAuthorization)
				{
					DebugLogger.Log($"[SnipeCommunicatorRequest] Waiting for login - {MessageType} - {Data?.ToJSONString()}");
					
					mCommunicator.LoginSucceeded -= OnCommunicatorReady;
					mCommunicator.LoginSucceeded += OnCommunicatorReady;
				}
				else if (mCommunicator.KeepOfflineRequests && !mAuthorization)
				{
					Active = false;
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

		private async void OnMessageReceived(string message_type, string error_code, SnipeObject response_data, int request_id)
		{
			if (mCommunicator == null)
				return;
			
			if (WaitingForRoomJoined && mCommunicator.RoomJoined == true)
			{
				WaitingForRoomJoined = false;
				DoSendRequest();
				return;
			}
			
			if ((request_id == 0 || request_id == mRequestId) && message_type == MessageType)
			{
				if (error_code == SnipeErrorCodes.SERVICE_OFFLINE && mRetriesLeft > 0)
				{
					mRetriesLeft--;

					await Task.Delay(RETRY_DELAY);

					Request(mCallback);

					return;
				}

				InvokeCallback(error_code, response_data);
			}
		}
		
		private void InvokeCallback(string error_code, SnipeObject response_data)
		{
			mCallback?.Invoke(error_code, response_data);
			Dispose();
		}

		public void Dispose()
		{
			if (mCommunicator != null)
			{
				if (mCommunicator.Requests != null)
				{
					mCommunicator.Requests.Remove(this);
				}
				
				mCommunicator.LoginSucceeded -= OnCommunicatorReady;
				mCommunicator.ConnectionFailed -= OnConnectionClosed;
				mCommunicator.MessageReceived -= OnMessageReceived;
				mCommunicator = null;
			}
			
			mCallback = null;
			mWaitingForResponse = false;
		}
	}
}