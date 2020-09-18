﻿using System;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeRequest : IDisposable
	{
		public const string ERROR_INVALIND_CLIENT = "invalidClient";
		public const string ERROR_NO_CONNECTION = "noConnection";
		public const string ERROR_NOT_LOGGED_IN = "notLoggedIn";
		public const string ERROR_INVALIND_DATA = "invalidData";
		
		protected static readonly ExpandoObject ErrorMessageInvalidClient = new ExpandoObject() { { "errorCode", ERROR_INVALIND_CLIENT } };
		protected static readonly ExpandoObject ErrorMessageNoConnection = new ExpandoObject() { { "errorCode", ERROR_NO_CONNECTION } };
		protected static readonly ExpandoObject ErrorMessageNotLoggedIn = new ExpandoObject() { { "errorCode", ERROR_NOT_LOGGED_IN } };
		protected static readonly ExpandoObject ErrorMessageInvalidData = new ExpandoObject() { { "errorCode", ERROR_INVALIND_DATA } };

		protected SnipeClient mClient;
		protected Action<ExpandoObject> mCallback;

		public string MessageType { get; protected set; }
		public ExpandoObject Data { get; set; }

		protected int mRequestId;

		public SnipeRequest(SnipeClient client, string message_type = null)
		{
			mClient = client;
			MessageType = message_type;
		}

		public void Request(ExpandoObject data, Action<ExpandoObject> callback = null)
		{
			Data = data;
			Request(callback);
		}

		public virtual void Request(Action<ExpandoObject> callback = null)
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
				MessageType = Data?.SafeGetString("messageType");

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
				MessageType = Data?.SafeGetString("messageType");

			return !string.IsNullOrEmpty(MessageType);
		}

		protected virtual void SetCallback(Action<ExpandoObject> callback)
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
		
		protected virtual void InvokeCallback(Action<ExpandoObject> callback, ExpandoObject response_data)
		{
			callback?.Invoke(response_data);
		}
		
		protected virtual void InvokeCallback(ExpandoObject response_data)
		{
			InvokeCallback(mCallback, response_data);
		}

		protected virtual void SendRequest()
		{
			mRequestId = mClient.SendRequest(MessageType, Data);
		}

		protected virtual void OnConnectionLost(ExpandoObject data)
		{
			if (mCallback != null)
			{
				InvokeCallback(ErrorMessageNoConnection);
				mCallback = null;
			}
		}

		protected void OnMessageReceived(ExpandoObject response_data)
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

		protected virtual bool CheckResponse(ExpandoObject response_data)
		{
			return (response_data.ContainsKey("_requestID") ? response_data.SafeGetValue<int>("_requestID") == mRequestId : response_data.SafeGetString("messageType") == MessageType);
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