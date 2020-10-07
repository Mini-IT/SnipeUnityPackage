using System;
using UnityEngine;
using MiniIT;
using System.Dynamic;

namespace MiniIT.Snipe
{
	public class SingleRequestClient : MonoBehaviour
	{
		private SnipeClient mClient;
		private ExpandoObject mRequestData;

		private Action<ExpandoObject> mCallback;

		private SingleRequestClient()
		{
		}

		public static void Request(SnipeServerConfig config, ExpandoObject request, Action<ExpandoObject> callback)
		{
			SnipeClient client = SnipeClient.CreateInstance(SnipeConfig.Instance.snipe_client_key, "SnipeSingleRequestClient", false);
			client.AppInfo = SnipeConfig.Instance.snipe_app_info;
			SingleRequestClient instance = client.gameObject.AddComponent<SingleRequestClient>();
			instance.InitClient(client, config, request, callback);
		}

		private void InitClient(SnipeClient client, SnipeServerConfig config, ExpandoObject request, Action<ExpandoObject> callback)
		{
			mRequestData = request;
			mCallback = callback;

			mClient = client;
			mClient.Init(config.host, config.port, config.websocket);
			mClient.ConnectionSucceeded += OnConnectionSucceeded;
			mClient.ConnectionFailed += OnConnectionFailed;
			mClient.ConnectionLost += OnConnectionFailed;
			mClient.Connect();
		}

		private void OnConnectionSucceeded(ExpandoObject data)
		{
			string request_message_type = mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);
			DebugLogger.Log($"[SingleRequestClient] ({request_message_type}) Connection succeeded");

			Analytics.TrackEvent(Analytics.EVENT_SINGLE_REQUEST_CLIENT_CONNECTED, new ExpandoObject()
			{
				["message_type"] = request_message_type,
				["connection_type"] = mClient.ConnectedViaWebSocket ? "websocket" : "tcp",
			});

			mClient.MessageReceived += OnResponse;
			mClient.SendRequest(mRequestData);
		}

		private void OnConnectionFailed(ExpandoObject data)
		{
			string request_message_type = mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);
			DebugLogger.Log($"[SingleRequestClient] ({request_message_type}) Connection failed");

			Analytics.TrackEvent(Analytics.EVENT_SINGLE_REQUEST_CLIENT_DISCONNECTED, new ExpandoObject()
			{
				["message_type"] = request_message_type,
			});

			mClient.MessageReceived -= OnResponse;

			InvokeCallback(new ExpandoObject() { ["errorCode"] = "connectionFailed" });
			DisposeClient();
		}

		private void OnResponse(ExpandoObject data)
		{
			string request_message_type = mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);
			string response_message_type = data.SafeGetString("type");
			DebugLogger.Log($"[SingleRequestClient] ({request_message_type}) OnResponse {data?.ToJSONString()}");

			Analytics.TrackEvent(Analytics.EVENT_SINGLE_REQUEST_RESPONSE, new ExpandoObject()
			{
				["message_type"] = request_message_type,
				["response_message_type"] = response_message_type,
				["error_code"] = data.SafeGetString("errorCode"),
				["connection_id"] = data.SafeGetString(SnipeClient.KEY_CONNECTION_ID),
			});

			if (response_message_type == request_message_type)
			{
				InvokeCallback(data);
				DisposeClient();
			}
		}

		private void InvokeCallback(ExpandoObject data)
		{
			if (mCallback != null)
				mCallback.Invoke(data);

			mCallback = null;
		}

		private void DisposeClient()
		{
			DebugLogger.Log($"[SingleRequestClient] ({mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE)}) DisposeClient");

			mCallback = null;
			mRequestData = null;

			if (mClient == null)
				return;

			mClient.MessageReceived -= OnResponse;
			mClient.ConnectionSucceeded -= OnConnectionSucceeded;
			mClient.ConnectionFailed -= OnConnectionFailed;
			mClient.ConnectionLost -= OnConnectionFailed;
			mClient.Dispose();
			mClient = null;
		}
	}
}