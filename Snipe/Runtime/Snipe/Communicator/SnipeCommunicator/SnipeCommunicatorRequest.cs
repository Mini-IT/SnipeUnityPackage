using System;
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

		public delegate void ResponseHandler(string error_code, ExpandoObject data = null);

		protected SnipeCommunicator mCommunicator;
		protected ResponseHandler mCallback;
		protected string mMessageType;

		protected int mRequestId;
		protected int mRetriesLeft = RETRIES_COUNT;

		public ExpandoObject Data { get; set; }

		public SnipeCommunicatorRequest(SnipeCommunicator communicator, string message_type = null)
		{
			mCommunicator = communicator;
			mMessageType = message_type;
			
			if (mCommunicator != null && mCommunicator.Requests != null)
				mCommunicator.Requests.Add(this);
		}

		public void Request(ExpandoObject data, ResponseHandler callback = null)
		{
			Data = data;
			Request(callback);
		}

		public virtual void Request(ResponseHandler callback = null)
		{
			if (mCommunicator == null || !mCommunicator.Connected)
			{
				callback?.Invoke(ERROR_NOT_READY);
				return;
			}
			
			if (string.IsNullOrEmpty(mMessageType))
				mMessageType = Data?.SafeGetString("t");

			if (string.IsNullOrEmpty(mMessageType))
			{
				callback?.Invoke(ERROR_INVALIND_DATA);
				return;
			}
			
			mCallback = callback;

			if (mCommunicator.LoggedIn)
			{
				OnCommunicatorReady();
			}
			else
			{
				mCommunicator.LoginSucceeded -= OnCommunicatorReady;
				mCommunicator.LoginSucceeded += OnCommunicatorReady;
			}
		}

		private async void OnCommunicatorReady()
		{
			if (mCommunicator?.Client == null)
			{
				mCallback?.Invoke(ERROR_NOT_READY);
				return;
			}

			if (mCallback != null)
			{
				mCommunicator.ConnectionFailed -= OnConnectionClosed;
				mCommunicator.ConnectionFailed += OnConnectionClosed;
				mCommunicator.MessageReceived -= OnMessageReceived;
				mCommunicator.MessageReceived += OnMessageReceived;
			}

			mRequestId = mCommunicator.Client.SendRequest(mMessageType, Data);
		}

		private void OnConnectionClosed(bool will_rety = false)
		{
			if (will_rety)
			{
				mCommunicator.LoginSucceeded -= OnCommunicatorReady;
				mCommunicator.LoginSucceeded += OnCommunicatorReady;
			}
			else
			{
				Dispose(true);
			}
		}

		protected async void OnMessageReceived(string message_type, string error_code, ExpandoObject response_data, int request_id)
		{
			if ((request_id == 0 || request_id == mRequestId) && message_type == mMessageType)
			{
				if (error_code == ERROR_SERVICE_OFFLINE && mRetriesLeft > 0)
				{
					mRetriesLeft--;

					await Task.Delay(RETRY_DELAY);

					Request(mCallback);

					return;
				}

				mCallback?.Invoke(error_code, response_data);

				Dispose();
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		
		public void Dispose(bool remove_from_list)
		{
			if (mCommunicator != null)
			{
				if (remove_from_list && mCommunicator.Requests != null && mCommunicator.Requests.Contains(this))
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