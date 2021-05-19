using System;
using System.Collections.Generic;
using System.Dynamic;
using UnityEngine;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SingleRequestClient : MonoBehaviour
	{
		private static Dictionary<string, WeakReference<SingleRequestClient>> mInstances;
		
		internal class RequestsQueueItem
		{
			internal SnipeObject mRequestData;
			internal Action<SnipeObject> mCallback;
		}

		private SnipeClient mClient;
		private SnipeObject mRequestData;
		private Action<SnipeObject> mCallback;

		private Queue<RequestsQueueItem> mRequestsQueue;

		private SingleRequestClient()
		{
		}

		public static void Request(string web_socket_url, SnipeObject request, Action<SnipeObject> callback)
		{
			if (mInstances != null &&
				mInstances.TryGetValue(web_socket_url, out var instance_reference) &&
				instance_reference != null &&
				instance_reference.TryGetTarget(out var old_instance))
			{
				if (old_instance?.mClient != null && old_instance.mClient.Connected)
				{
					if (old_instance.mRequestData == null)
					{
						old_instance.mRequestData = request;
						old_instance.mCallback = callback;
						old_instance.mClient.SendRequest(request);
					}
					else
					{
						if (old_instance.mRequestsQueue == null)
							old_instance.mRequestsQueue = new Queue<RequestsQueueItem>();

						old_instance.mRequestsQueue.Enqueue(new RequestsQueueItem()
						{
							mRequestData = request,
							mCallback = callback,
						});
					}
					return;
				}
				else
				{
					mInstances.Remove(web_socket_url);
				}
			}
			
			if (mInstances == null)
				mInstances = new Dictionary<string, WeakReference<SingleRequestClient>>();

			SnipeClient client = SnipeClient.CreateInstance(SnipeConfig.Instance.ClientKey, "SnipeSingleRequestClient", false);
			SingleRequestClient instance = client.gameObject.AddComponent<SingleRequestClient>();
			instance.InitClient(client, web_socket_url, request, callback);

			mInstances[web_socket_url] = new WeakReference<SingleRequestClient>(instance);
		}

		private void InitClient(SnipeClient client, string web_socket_url, SnipeObject request, Action<SnipeObject> callback)
		{
			mRequestData = request;
			mCallback = callback;

			mClient = client;
			mClient.Init(web_socket_url);
			mClient.ConnectionSucceeded += OnConnectionSucceeded;
			mClient.ConnectionFailed += OnConnectionFailed;
			mClient.ConnectionLost += OnConnectionFailed;
			mClient.Connect();
		}

		private void OnConnectionSucceeded(SnipeObject data)
		{
			string request_message_type = mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);
			DebugLogger.Log($"[SingleRequestClient] ({request_message_type}) Connection succeeded");

			Analytics.TrackEvent(Analytics.EVENT_SINGLE_REQUEST_CLIENT_CONNECTED, new SnipeObject()
			{
				["connection_id"] = mClient.ConnectionId,
				["message_type"] = request_message_type,
				["connection_type"] = "websocket",
			});

			mClient.MessageReceived += OnResponse;
			mClient.SendRequest(mRequestData);
		}

		private void OnConnectionFailed(SnipeObject data)
		{
			string request_message_type = mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);
			DebugLogger.Log($"[SingleRequestClient] ({request_message_type}) Connection failed");

			Analytics.TrackEvent(Analytics.EVENT_SINGLE_REQUEST_CLIENT_DISCONNECTED, new SnipeObject()
			{
				["connection_id"] = mClient != null ? mClient.ConnectionId : "",
				["message_type"] = request_message_type,
			});

			mClient.MessageReceived -= OnResponse;

			InvokeCallback(new SnipeObject() { ["errorCode"] = "connectionFailed" });
			DisposeClient();
		}

		private void OnResponse(SnipeObject data)
		{
			string request_message_type = mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE);
			string response_message_type = data.SafeGetString("type");
			DebugLogger.Log($"[SingleRequestClient] ({request_message_type}) OnResponse {data?.ToJSONString()}");

			Analytics.TrackEvent(Analytics.EVENT_SINGLE_REQUEST_RESPONSE, new SnipeObject()
			{
				["message_type"] = request_message_type,
				["response_message_type"] = response_message_type,
				["error_code"] = data?.SafeGetString("errorCode"),
				["connection_id"] = data?.SafeGetString(SnipeClient.KEY_CONNECTION_ID),
			});

			if (response_message_type == request_message_type)
			{
				InvokeCallback(data);

				if (mRequestsQueue != null && mRequestsQueue.Count > 0)
				{
					var item = mRequestsQueue.Dequeue();
					mRequestData = item.mRequestData;
					mCallback = item.mCallback;
					mClient.SendRequest(mRequestData);
				}
			}
		}

		private void InvokeCallback(SnipeObject data)
		{
			if (mCallback != null)
				mCallback.Invoke(data);

			mCallback = null;
			mRequestData = null;
		}

		private void DisposeClient()
		{
			DebugLogger.Log($"[SingleRequestClient] ({mRequestData?.SafeGetString(SnipeClient.KEY_MESSAGE_TYPE)}) DisposeClient");

			mCallback = null;
			mRequestData = null;

			if (mClient == null)
				return;

			if (mInstances != null && !string.IsNullOrEmpty(mClient.mConnectionWebSocketURL))
			{
				mInstances.Remove(mClient.mConnectionWebSocketURL);
			}

			mClient.MessageReceived -= OnResponse;
			mClient.ConnectionSucceeded -= OnConnectionSucceeded;
			mClient.ConnectionFailed -= OnConnectionFailed;
			mClient.ConnectionLost -= OnConnectionFailed;
			mClient.Dispose();
			mClient = null;
		}
	}
}