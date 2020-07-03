using System;
using MiniIT;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeCommunicatorRequest : SnipeRequest
	{
		protected SnipeCommunicator mCommunicator;
		protected bool mSendRequestProcessed = false;
		
		public SnipeCommunicatorRequest(SnipeCommunicator communicator, string message_type = null) : base(communicator.Client, message_type)
		{
			mCommunicator = communicator;
		}

		public override void Request(Action<ExpandoObject> callback = null)
		{
			if (mSendRequestProcessed)
				return;
			
			if (!CheckMessageType())
			{
				if (callback != null)
					callback.Invoke(ErrorMessageInvalidData);
				return;
			}

			SetCallback(callback);
			SendRequest();
		}
		
		protected virtual void SetCallback(Action<ExpandoObject> callback)
		{
			mCallback = callback;
			if (mCallback != null && mClient != null)
			{
				mClient.MessageReceived -= OnMessageReceived;
				mClient.MessageReceived += OnMessageReceived;
			}
		}

		protected override void SendRequest()
		{
			if (mSendRequestProcessed)
				return;
			mSendRequestProcessed = true;
			
			if (mCommunicator == null)
			{
				mCallback?.Invoke(ErrorMessageInvalidClient);
				return;
			}

			if (!CheckMessageType())
			{
				mCallback?.Invoke(ErrorMessageInvalidData);
				return;
			}
			
			mCommunicator.ConnectionFailed -= OnCommunicatorConnectionLost;
			mCommunicator.ConnectionFailed += OnCommunicatorConnectionLost;

			if (mCommunicator.LoggedIn || MessageType == "user.login")
			{
				mRequestId = mCommunicator.Request(this);
			}
			else
			{
				AddOnLoginSucceededListener();
			}
		}
		
		protected void OnCommunicatorConnectionLost(bool will_restore)
		{
			AddOnLoginSucceededListener();
		}
		
		private void AddOnLoginSucceededListener()
		{
			if (mCommunicator == null)
				return;
			
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
				mCommunicator.ConnectionFailed -= OnCommunicatorConnectionLost;
			}
			
			if (mCallback != null)
			{
				mCallback.Invoke(ErrorMessageInvalidClient);
				mCallback = null;
			}
		}

		public override void Dispose()
		{
			mCallback = null;
			
			OnCommunicatorPreDestroy();

			base.Dispose();
		}
	}
}