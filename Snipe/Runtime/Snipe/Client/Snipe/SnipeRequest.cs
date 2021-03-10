using System;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeRequest : IDisposable
	{
		public const string ERROR_INVALIND_CLIENT = "invalidClient";
		public const string ERROR_NO_CONNECTION = "noConnection";
		public const string ERROR_NOT_LOGGED_IN = "notLoggedIn";
		public const string ERROR_INVALIND_DATA = "invalidData";
		
		protected static readonly SnipeObject ErrorMessageInvalidClient = new SnipeObject() { { "errorCode", ERROR_INVALIND_CLIENT } };
		protected static readonly SnipeObject ErrorMessageNoConnection = new SnipeObject() { { "errorCode", ERROR_NO_CONNECTION } };
		protected static readonly SnipeObject ErrorMessageNotLoggedIn = new SnipeObject() { { "errorCode", ERROR_NOT_LOGGED_IN } };
		protected static readonly SnipeObject ErrorMessageInvalidData = new SnipeObject() { { "errorCode", ERROR_INVALIND_DATA } };

		protected SnipeClient mClient;
		protected Action<SnipeObject> mCallback;

		public string MessageType { get; protected set; }
		public SnipeObject Data { get; set; }

		protected int mRequestId;

		public SnipeRequest(SnipeClient client, string message_type = null)
		{
			mClient = client;
			MessageType = message_type;
		}

		public void Request(SnipeObject data, Action<SnipeObject> callback = null)
		{
			Data = data;
			Request(callback);
		}

		public virtual void Request(Action<SnipeObject> callback = null)
		{
			if (mClient == null)
			{
				InvokeCallback(callback, ErrorMessageInvalidClient);
				return;
			}
			
			if (!mClient.Connected)
			{
				InvokeCallback(callback, ErrorMessageNoConnection);
				return;
			}

			if (!mClient.LoggedIn)
			{
				InvokeCallback(callback, ErrorMessageNotLoggedIn);
				return;
			}

			if (string.IsNullOrEmpty(MessageType))
				MessageType = Data?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);

			if (!CheckMessageType())
			{
				InvokeCallback(callback, ErrorMessageInvalidData);
				return;
			}

			SetCallback(callback);
			SendRequest();
		}

		protected bool CheckMessageType()
		{
			if (string.IsNullOrEmpty(MessageType))
				MessageType = Data?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);

			return !string.IsNullOrEmpty(MessageType);
		}

		protected virtual void SetCallback(Action<SnipeObject> callback)
		{
			mCallback = callback;
			if (mClient != null)
			{
				mClient.ConnectionLost -= OnConnectionLost;
				mClient.MessageReceived -= OnMessageReceived;
				mClient.ConnectionLost += OnConnectionLost;
				mClient.MessageReceived += OnMessageReceived;
			}
		}
		
		protected virtual void InvokeCallback(Action<SnipeObject> callback, SnipeObject response_data)
		{
			callback?.Invoke(response_data);
		}
		
		protected virtual void InvokeCallback(SnipeObject response_data)
		{
			InvokeCallback(mCallback, response_data);
		}

		protected virtual void SendRequest()
		{
			mRequestId = mClient.SendRequest(MessageType, Data);
		}

		protected virtual void OnConnectionLost(SnipeObject data)
		{
			if (mCallback != null)
			{
				InvokeCallback(ErrorMessageNoConnection);
				mCallback = null;
			}
		}

		protected void OnMessageReceived(SnipeObject response_data)
		{
			if (CheckResponse(response_data))
			{
				try
				{
					InvokeCallback(response_data);
				}
				catch (Exception) { } // Ignore any exception
				finally
				{
					Dispose();
				}
			}
		}

		protected virtual bool CheckResponse(SnipeObject response_data)
		{
			return (response_data.ContainsKey(SnipeClient.KEY_REQUEST_ID) ?
				response_data.SafeGetValue<int>(SnipeClient.KEY_REQUEST_ID) == mRequestId :
				response_data.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE) == MessageType);
		}

		public virtual void Dispose()
		{
			mCallback = null;
			
			if (mClient != null)
			{
				mClient.ConnectionLost -= OnConnectionLost;
				mClient.MessageReceived -= OnMessageReceived;
				mClient = null;
			}
		}
	}
}