﻿using System;
using System.Threading.Tasks;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeCommunicatorRequest : IDisposable
	{
		public const string ERROR_NOT_READY = "notReady";
		public const string ERROR_INVALIND_DATA = "invalidData";
		public const string ERROR_SERVICE_OFFLINE = "serviceOffline";

		private const int RETRIES_COUNT = 3;
		private const int RETRY_DELAY = 1000; // milliseconds
		
		private static readonly ExpandoObject EMPTY_DATA = new ExpandoObject();
		
		public bool Active { get; private set; } = true;
		public string MessageType { get; private set; }
		public ExpandoObject Data { get; set; }
		
		public delegate void ResponseHandler(string error_code, ExpandoObject data);

		private SnipeCommunicator mCommunicator;
		private ResponseHandler mCallback;

		private int mRequestId;
		private int mRetriesLeft = RETRIES_COUNT;
		
		private bool mSent = false;

		public SnipeCommunicatorRequest(SnipeCommunicator communicator, string message_type = null)
		{
			mCommunicator = communicator;
			MessageType = message_type;
			
			if (mCommunicator != null)
			{	
				mCommunicator.Requests.Add(this);
			}
		}

		public void Request(ExpandoObject data, ResponseHandler callback = null)
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
			
			if (mCommunicator == null) // || !mCommunicator.Connected)
			{
				InvokeCallback(ERROR_NOT_READY, EMPTY_DATA);
				return;
			}
			
			if (string.IsNullOrEmpty(MessageType))
				MessageType = Data?.SafeGetString("t");

			if (string.IsNullOrEmpty(MessageType))
			{
				InvokeCallback(ERROR_INVALIND_DATA, EMPTY_DATA);
				return;
			}
			
			if (mCommunicator.LoggedIn)
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
			if (mCallback != null)
			{
				mCommunicator.ConnectionFailed -= OnConnectionClosed;
				mCommunicator.ConnectionFailed += OnConnectionClosed;
				mCommunicator.MessageReceived -= OnMessageReceived;
				mCommunicator.MessageReceived += OnMessageReceived;
			}
			
			mRequestId = mCommunicator.Request(this);
			
			if (mRequestId == 0)
			{
				InvokeCallback(ERROR_NOT_READY, EMPTY_DATA);
			}
		}

		private void OnConnectionClosed(bool will_rety = false)
		{
			if (will_rety)
			{
				if (mCommunicator.AllowRequestsToWaitForLogin)
				{
					mCommunicator.LoginSucceeded -= OnCommunicatorReady;
					mCommunicator.LoginSucceeded += OnCommunicatorReady;
				}
				else if (mCommunicator.KeepOfflineRequests)
				{
					Active = false;
				}
				else
				{
					InvokeCallback(ERROR_NOT_READY, EMPTY_DATA);
				}
			}
			else
			{
				Dispose();
			}
		}

		private async void OnMessageReceived(string message_type, string error_code, ExpandoObject response_data, int request_id)
		{
			if ((request_id == 0 || request_id == mRequestId) && message_type == MessageType)
			{
				if (error_code == ERROR_SERVICE_OFFLINE && mRetriesLeft > 0)
				{
					mRetriesLeft--;

					await Task.Delay(RETRY_DELAY);

					Request(mCallback);

					return;
				}

				InvokeCallback(error_code, response_data);
			}
		}
		
		private void InvokeCallback(string error_code, ExpandoObject response_data)
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
		}
	}
}