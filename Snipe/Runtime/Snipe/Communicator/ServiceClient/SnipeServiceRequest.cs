using System;
using System.Threading.Tasks;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeServiceRequest : IDisposable
	{
		public const string ERROR_NOT_READY = "notReady";
		public const string ERROR_INVALIND_DATA = "invalidData";
		public const string ERROR_SERVCIE_OFFLINE = "serviceOffline";

		private const int RETRIES_COUNT = 3;
		private const int RETRY_DELAY = 1000; // milliseconds

		protected SnipeServiceCommunicator mCommunicator;
		protected Action<ExpandoObject> mCallback;
		protected string mMessageType;

		protected int mRequestId;
		protected int mRetriesLeft = RETRIES_COUNT;

		public ExpandoObject Data { get; set; }

		public SnipeServiceRequest(SnipeServiceCommunicator communicator, string message_type = null)
		{
			mCommunicator = communicator;
			mMessageType = message_type;
			
			if (mCommunicator != null && mCommunicator.Requests != null)
				mCommunicator.Requests.Add(this);
		}

		public void Request(ExpandoObject data, Action<ExpandoObject> callback = null)
		{
			Data = data;
			Request(callback);
		}

		public virtual void Request(Action<ExpandoObject> callback = null)
		{
			if (mCommunicator == null || !mCommunicator.Ready)
			{
				if (callback != null)
					callback.Invoke(new ExpandoObject() { { "errorCode", ERROR_NOT_READY } });
				return;
			}
			
			if (string.IsNullOrEmpty(mMessageType))
				mMessageType = Data?.SafeGetString("t");

			if (string.IsNullOrEmpty(mMessageType))
			{
				if (callback != null)
					callback.Invoke(new ExpandoObject() { { "errorCode", ERROR_INVALIND_DATA } });
				return;
			}
			
			mCallback = callback;
			
			if (mCommunicator.Ready)
			{
				OnCommunicatorReady();
			}
			else
			{
				mCommunicator.AddReadyCallback(OnCommunicatorReady);
			}
		}
		
		private void OnCommunicatorReady()
		{
			if (mCallback != null)
			{
				mCommunicator.ConnectionClosed -= OnConnectionClosed;
				mCommunicator.ConnectionClosed += OnConnectionClosed;
				mCommunicator.MessageReceived -= OnMessageReceived;
				mCommunicator.MessageReceived += OnMessageReceived;
			}
			mRequestId = mCommunicator.Client.SendRequest(mMessageType, Data);
		}

		private void OnConnectionClosed()
		{
			if (mCommunicator != null)
				mCommunicator.AddReadyCallback(OnCommunicatorReady);
		}

		protected void OnMessageReceived(ExpandoObject response_data)
		{
			if (CheckResponse(response_data))
			{
				if (response_data.SafeGetString("errorCode") == ERROR_SERVCIE_OFFLINE && mRetriesLeft > 0)
				{
					mRetriesLeft--;

					Task.Delay(RETRY_DELAY).ContinueWith((task) =>
					{
						Request(mCallback);
					});

					return;
				}

				if (mCallback != null)
					mCallback.Invoke(response_data);

				Dispose();
			}
		}

		protected virtual bool CheckResponse(ExpandoObject response_data)
		{
			int request_id = response_data.SafeGetValue<int>("id");
			return ((request_id == 0 || request_id == mRequestId) && response_data.SafeGetString("t") == mMessageType);
		}

		public void Dispose()
		{
			Dispose(true);
		}
		
		public void Dispose(bool remove_from_list)
		{
			if (mCommunicator != null)
			{
				if (mCommunicator.Requests != null && mCommunicator.Requests.Contains(this))
					mCommunicator.Requests.Remove(this);
				
				mCommunicator.ConnectionClosed -= OnConnectionClosed;
				mCommunicator.MessageReceived -= OnMessageReceived;
				mCommunicator = null;
			}
			
			mCallback = null;
		}
	}
}