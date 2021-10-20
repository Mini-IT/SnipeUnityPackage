﻿using System;
using System.Threading.Tasks;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeRequest : IDisposable
	{
		private const int RETRIES_COUNT = 3;
		private const int RETRY_DELAY = 1000; // milliseconds
		
		private static readonly SnipeObject EMPTY_DATA = new SnipeObject();
		
		public string MessageType { get; private set; }
		public SnipeObject Data { get; set; }
		
		//public bool WaitingForRoomJoined { get; private set; } = false;
		
		public delegate void ResponseHandler(string error_code, SnipeObject data);

		private SnipeChannel mChannel;
		private ResponseHandler mCallback;

		private int mRequestId;
		private int mRetriesLeft = RETRIES_COUNT;
		
		private bool mConstructed = false;
		private bool mWaitingForResponse = false;
		//private bool mNoScope = false;

		public SnipeRequest(SnipeChannel channel, string message_type = null)
		{
			mChannel = channel ?? SnipeCommunicator.Instance.MainChannel;
			MessageType = message_type;
			
			if (mChannel != null)
			{	
				mChannel.Requests.Add(this);
			}
		}

		public void Request(SnipeObject data, ResponseHandler callback = null)
		{
			Data = data;
			Request(callback);
		}
		
		public void Request(ResponseHandler callback = null)
		{
			if (mConstructed)
				return;
				
			mCallback = callback;
			ConstructRequest();
		}
		
		// public void RequestNoScope(SnipeObject data, ResponseHandler callback = null)
		// {
			// mNoScope = true;
			// Data = data;
			// Request(callback);
		// }
		
		// public void RequestNoScope(ResponseHandler callback = null)
		// {
			// mNoScope = true;
			// Request(callback);
		// }
		
		private void ConstructRequest()
		{
			mConstructed = true;
			
			if (!SnipeCommunicator.InstanceInitialized)
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
				return;
			}
			
			if (string.IsNullOrEmpty(MessageType))
			{
				MessageType = Data?.SafeGetString("t");
			}

			if (string.IsNullOrEmpty(MessageType))
			{
				InvokeCallback(SnipeErrorCodes.INVALIND_DATA, EMPTY_DATA);
				return;
			}
			
			//if (!SnipeCommunicator.InstanceInitialized || SnipeCommunicator.Instance.RoomJoined == false && MessageType == SnipeMessageTypes.ROOM_LEAVE)
			//if (!SnipeCommunicator.InstanceInitialized || (!mNoScope && !mChannel.CheckReady()))
			// if (!mChannel.CheckNoScopeMessageType(MessageType) && !mChannel.CheckReady())
			// {
				// InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
				// return;
			// }
			
			if (mChannel.CheckReady(MessageType))
			{
				OnChannelReady();
			}
			else
			{
				OnConnectionClosed(true);
			}
		}

		internal void OnChannelReady()
		{
			if (mRequestId != 0)
				return;
			
			// if (SnipeCommunicator.Instance.RoomJoined != true &&
				// MessageType.StartsWith(SnipeMessageTypes.PREFIX_ROOM) &&
				// MessageType != SnipeMessageTypes.ROOM_JOIN &&
				// MessageType != SnipeMessageTypes.ROOM_LEAVE)
			// {
				// WaitingForRoomJoined = true;
			// }
			
			// SnipeCommunicator.Instance.ConnectionFailed -= OnConnectionClosed;
			// SnipeCommunicator.Instance.ConnectionFailed += OnConnectionClosed;
			
			// if ((mCallback != null || WaitingForRoomJoined) && MessageType != SnipeMessageTypes.ROOM_LEAVE)
			// {
				// mWaitingForResponse = true;
				// SnipeCommunicator.Instance.MessageReceived -= OnMessageReceived;
				// SnipeCommunicator.Instance.MessageReceived += OnMessageReceived;
			// }
			
			// if (!WaitingForRoomJoined)
			// {
				SendRequest();
			// }
		}
		
		private void SendRequest()
		{
			mRequestId = 0;
			
			bool check_duplication = !string.Equals(this.MessageType, SnipeMessageTypes.LOGIC_INC_VAR, StringComparison.Ordinal);
			
			if (check_duplication && SnipeCommunicator.Instance.mDontMergeRequests != null)
			{
				for (int i = 0; i < SnipeCommunicator.Instance.mDontMergeRequests.Count; i++)
				{
					string skip_message_type = SnipeCommunicator.Instance.mDontMergeRequests[i];
					if (skip_message_type != null && string.Equals(skip_message_type, this.MessageType, StringComparison.Ordinal))
					{
						check_duplication = false;
						break;
					}
				}
			}
			
			if (check_duplication)
			{
				for (int i = 0; i < mChannel.Requests.Count; i++)
				{
					var request = mChannel.Requests[i];
					
					if (request == null)
						continue;
					
					if (request == this)
						break;
					
					if (string.Equals(request.MessageType, MessageType, StringComparison.Ordinal) &&
						SnipeObject.ContentEquals(request.Data, Data))
					{
						mRequestId = request.mRequestId;
						break;
					}
				}
			}
			
			if (mRequestId != 0)
			{
				DebugLogger.Log($"[SnipeRequest] ({mChannel?.Name}) SendRequest - Same request found: {MessageType}, id = {mRequestId}");
				
				if (!mWaitingForResponse)
				{
					Dispose();
				}
				return;
			}
			
			if (mChannel.CheckReady(MessageType))
			{
				mRequestId = SnipeCommunicator.Instance.Client.SendRequest(MessageType, Data);

				DebugLogger.Log($"[SnipeRequest] ({mChannel?.Name}) SendRequest - {MessageType}, id = {mRequestId}");
			}
			
			if (mRequestId == 0)
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
			}
			else if (!mWaitingForResponse)
			{
				// keep this instance for a while to prevent duplicate requests
				DelayedDispose();
			}
		}

		private void OnConnectionClosed(bool will_rety = false)
		{
			if (will_rety)
			{
				mWaitingForResponse = false;

				SnipeCommunicator.Instance.MessageReceived -= OnMessageReceived;
				
				if (mChannel is SnipeAuthChannel)
				{
					DebugLogger.Log($"[SnipeRequest] ({mChannel?.Name}) Waiting for channel ready - {MessageType}");
				}
				else if (mChannel.KeepRequestsIfNotReady) //(SnipeCommunicator.Instance.AllowRequestsToWaitForLogin)
				{
					DebugLogger.Log($"[SnipeRequest] ({mChannel?.Name}) Waiting for channel ready - {MessageType} - {Data?.ToJSONString()}");
				}
				else
				{
					DebugLogger.Log($"[SnipeRequest] ({mChannel?.Name}) Channel not ready - {MessageType}");
					
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
			if (!SnipeCommunicator.InstanceInitialized)
				return;
			
			// if (WaitingForRoomJoined && SnipeCommunicator.Instance.RoomJoined == true)
			// {
				// DebugLogger.Log($"[SnipeRequest] OnMessageReceived - Room joined. Send {MessageType}, id = {mRequestId}");
				
				// WaitingForRoomJoined = false;
				// SendRequest();
				// return;
			// }
			
			if ((request_id == 0 || request_id == mRequestId) && message_type == MessageType)
			{
				if (error_code == SnipeErrorCodes.SERVICE_OFFLINE && mRetriesLeft > 0)
				{
					mRetriesLeft--;
					
					DelayedRetryRequest();
					return;
				}

				InvokeCallback(error_code, response_data);
			}
		}
		
		private void InvokeCallback(string error_code, SnipeObject response_data)
		{
			var callback = mCallback;
			
			Dispose();
			
			if (callback != null)
			{
				try
				{
					callback.Invoke(error_code, response_data);
				}
				catch (Exception e)
				{
					DebugLogger.Log($"[SnipeRequest] ({mChannel?.Name}) {MessageType} Callback invokation error: {e.Message}");
				}
			}
		}
		
		private async void DelayedRetryRequest()
		{
			await Task.Delay(RETRY_DELAY);
			Request(mCallback);
		}
		
		private async void DelayedDispose()
		{
			await Task.Yield();
			Dispose();
		}

		public void Dispose()
		{
			if (mChannel != null)
			{
				if (SnipeCommunicator.InstanceInitialized)
				{
					SnipeCommunicator.Instance.ConnectionFailed -= OnConnectionClosed;
					SnipeCommunicator.Instance.MessageReceived -= OnMessageReceived;
				}
				
				mChannel.Requests.Remove(this);
				mChannel = null;
			}
			
			mCallback = null;
			mWaitingForResponse = false;
		}
	}
}