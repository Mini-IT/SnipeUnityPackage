using System;
using MiniIT;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeCommunicatorRequest : SnipeRequest
	{
		public bool Active { get; protected set; } = true;
		
		protected SnipeCommunicator mCommunicator;
		protected bool mSendRequestProcessed = false;
		
		public SnipeCommunicatorRequest(SnipeCommunicator communicator, string message_type = null) : base(communicator.Client, message_type)
		{
			mCommunicator = communicator;
			mCommunicator.Requests.Add(this);
		}
		
		protected void RemoveFromActiveRequests()
		{
			mCommunicator?.Requests?.Remove(this);
		}
		
		protected override void InvokeCallback(Action<ExpandoObject> callback, ExpandoObject response_data)
		{
			RemoveFromActiveRequests();
			base.InvokeCallback(callback, response_data);
		}

		public override void Request(Action<ExpandoObject> callback = null)
		{
			if (mSendRequestProcessed)
				return;
			
			if (!CheckMessageType())
			{
				InvokeCallback(callback, ErrorMessageInvalidData);
				return;
			}

			SetCallback(callback);
			SendRequest();
		}
		
		internal void ResendInactive()
		{
			if (!Active)
			{
				Active = true;
				mSendRequestProcessed = false;
				SendRequest();
			}
		}

		protected override void SendRequest()
		{
			if (mSendRequestProcessed)
				return;
			mSendRequestProcessed = true;
			
			if (mCommunicator == null)
			{
				InvokeCallback(ErrorMessageInvalidClient);
				return;
			}

			if (!CheckMessageType())
			{
				InvokeCallback(ErrorMessageInvalidData);
				return;
			}
			
			if (mCommunicator.LoggedIn || MessageType == "user.login")
			{
				mRequestId = mCommunicator.Request(this);
			}
			else
			{
				AddOnLoginSucceededListener();
			}
		}
		
		protected override void OnConnectionLost(ExpandoObject data)
		{
			AddOnLoginSucceededListener();
		}
		
		private void AddOnLoginSucceededListener()
		{
			if (mCommunicator == null)
			{
				InvokeCallback(ErrorMessageInvalidClient);
				Dispose();
				return;
			}
			
			if (!mCommunicator.AllowRequestsToWaitForLogin)
			{
				if (mCommunicator.KeepOfflineRequests)
				{
					Active = false;
					return;
				}
				
				InvokeCallback(mCommunicator.Connected ? ErrorMessageNotLoggedIn : ErrorMessageNoConnection);
				Dispose();
				return;
			}
			
			if (mCommunicator is SnipeRoomCommunicator room_communicator)
			{
				room_communicator.RoomJoined -= OnCommunicatorLoginSucceeded;
				room_communicator.RoomJoined += OnCommunicatorLoginSucceeded;
			}
			else
			{
				mCommunicator.LoginSucceeded -= OnCommunicatorLoginSucceeded;
				mCommunicator.LoginSucceeded += OnCommunicatorLoginSucceeded;
			}

			mCommunicator.PreDestroy -= OnCommunicatorPreDestroy;
			mCommunicator.PreDestroy += OnCommunicatorPreDestroy;
		}

		private void OnCommunicatorLoginSucceeded()
		{
			if (mCommunicator != null)
			{
				if (mCommunicator is SnipeRoomCommunicator room_communicator)
				{
					room_communicator.RoomJoined -= OnCommunicatorLoginSucceeded;
				}

				mCommunicator.LoginSucceeded -= OnCommunicatorLoginSucceeded;
				
				// update client instance and event listeners
				mClient = mCommunicator.Client;
				SetCallback(mCallback);
				
				mRequestId = mCommunicator.Request(this);
			}
		}

		private void OnCommunicatorPreDestroy()
		{
			if (mCommunicator != null)
			{
				if (mCommunicator is SnipeRoomCommunicator room_communicator)
					room_communicator.RoomJoined -= OnCommunicatorLoginSucceeded;

				mCommunicator.LoginSucceeded -= OnCommunicatorLoginSucceeded;
				mCommunicator.PreDestroy -= OnCommunicatorPreDestroy;
			}
			
			if (mCallback != null)
			{
				InvokeCallback(ErrorMessageInvalidClient);
				mCallback = null;
			}
		}

		public override void Dispose()
		{
			RemoveFromActiveRequests();
			
			base.Dispose();
			
			OnCommunicatorPreDestroy();
			mCommunicator = null;
		}
	}
}