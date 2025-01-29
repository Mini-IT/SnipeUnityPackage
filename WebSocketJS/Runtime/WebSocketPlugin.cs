#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;

namespace WebSocketJS
{
	public static class WebSocketPlugin
	{
		[DllImport("__Internal")]
		public static extern int WebSocketConnect(int instanceId);

		[DllImport("__Internal")]
		public static extern int WebSocketClose(int instanceId, int code, string reason);

		[DllImport("__Internal")]
		public static extern int WebSocketSend(int instanceId, byte[] dataPtr, int dataLength);

		[DllImport("__Internal")]
		public static extern int WebSocketGetState(int instanceId);

		[DllImport("__Internal")]
		public static extern int WebSocketAllocate(string url);

		[DllImport("__Internal")]
		public static extern void WebSocketFree(int instanceId);

		[DllImport("__Internal")]
		public static extern void WebSocketSetOnOpen(OnOpenCallback callback);

		[DllImport("__Internal")]
		public static extern void WebSocketSetOnMessage(OnMessageCallback callback);

		[DllImport("__Internal")]
		public static extern void WebSocketSetOnError(OnErrorCallback callback);

		[DllImport("__Internal")]
		public static extern void WebSocketSetOnClose(OnCloseCallback callback);

		public delegate void OnOpenCallback(int instanceId);
		public delegate void OnMessageCallback(int instanceId, IntPtr msgPtr, int msgSize);
		public delegate void OnErrorCallback(int instanceId, IntPtr errorPtr);
		public delegate void OnCloseCallback(int instanceId, int closeCode);

		private static readonly Dictionary<int, WebSocket> s_instances = new Dictionary<int, WebSocket>();

		private static bool s_initialized = false;
		
		private static void Initialize()
		{
			WebSocketSetOnOpen(OnWebSocketOpen);
			WebSocketSetOnMessage(OnWebSocketMessage);
			WebSocketSetOnError(OnWebSocketError);
			WebSocketSetOnClose(OnWebSocketClose);

			s_initialized = true;
		}

		public static WebSocket CreateInstance(string url)
		{
			if (!s_initialized)
			{
				Initialize();
			}

			int instanceId = WebSocketAllocate(url);
			var instance = new WebSocket(instanceId);
			s_instances.Add(instanceId, instance);

			return instance;
		}

		public static void DestroyInstance(int instanceId)
		{
			s_instances.Remove(instanceId);
			WebSocketFree(instanceId);
		}

		[MonoPInvokeCallback(typeof(OnOpenCallback))]
		public static void OnWebSocketOpen(int instanceId)
		{
			if (s_instances.TryGetValue(instanceId, out WebSocket instance))
			{
				instance.RaiseOnOpenEvent();
			}
		}

		[MonoPInvokeCallback(typeof(OnMessageCallback))]
		public static void OnWebSocketMessage(int instanceId, IntPtr msgPtr, int msgSize)
		{
			if (s_instances.TryGetValue(instanceId, out WebSocket instance))
			{
				byte[] msg = new byte[msgSize];
				Marshal.Copy(msgPtr, msg, 0, msgSize);

				instance.RaiseOnMessageEvent(msg);
			}
		}

		[MonoPInvokeCallback(typeof(OnErrorCallback))]
		public static void OnWebSocketError(int instanceId, IntPtr errorPtr)
		{
			if (s_instances.TryGetValue(instanceId, out WebSocket instance))
			{
				string errorMsg = Marshal.PtrToStringAuto(errorPtr);
				instance.RaiseOnErrorEvent(errorMsg);
			}
		}

		[MonoPInvokeCallback(typeof(OnCloseCallback))]
		public static void OnWebSocketClose(int instanceId, int closeCode)
		{
			if (s_instances.TryGetValue(instanceId, out WebSocket instance))
			{
				instance.RaiseOnCloseEvent(closeCode);
			}
		}
	}
}

#endif
