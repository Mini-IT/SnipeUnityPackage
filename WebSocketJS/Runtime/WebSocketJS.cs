#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;

namespace WebSocketJS
{
	/// <summary>
	/// Handler for WebSocket Open event.
	/// </summary>
	public delegate void WebSocketOpenEventHandler();

	/// <summary>
	/// Handler for message received from WebSocket.
	/// </summary>
	public delegate void WebSocketMessageEventHandler(byte[] data);

	/// <summary>
	/// Handler for an error event received from WebSocket.
	/// </summary>
	public delegate void WebSocketErrorEventHandler(string errorMsg);

	/// <summary>
	/// Handler for WebSocket Close event.
	/// </summary>
	public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);

	/// <summary>
	/// Enum representing WebSocket connection state
	/// </summary>
	public enum WebSocketState
	{
		Connecting,
		Open,
		Closing,
		Closed
	}

	/// <summary>
	/// Web socket close codes.
	/// </summary>
	public enum WebSocketCloseCode
	{
		/* Do NOT use NotSet - it's only purpose is to indicate that the close code cannot be parsed. */
		NotSet = 0,
		Normal = 1000,
		Away = 1001,
		ProtocolError = 1002,
		UnsupportedData = 1003,
		Undefined = 1004,
		NoStatus = 1005,
		Abnormal = 1006,
		InvalidData = 1007,
		PolicyViolation = 1008,
		TooBig = 1009,
		MandatoryExtension = 1010,
		ServerError = 1011,
		TlsHandshakeFailure = 1015
	}

	public class WebSocket
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

		private static Dictionary<int, WebSocket> instances = new Dictionary<int, WebSocket>();

		protected int instanceId;

		public event WebSocketOpenEventHandler OnOpen;
		public event WebSocketMessageEventHandler OnMessage;
		public event WebSocketErrorEventHandler OnError;
		public event WebSocketCloseEventHandler OnClose;

		public delegate void OnOpenCallback(int instanceId);
		public delegate void OnMessageCallback(int instanceId, System.IntPtr msgPtr, int msgSize);
		public delegate void OnErrorCallback(int instanceId, System.IntPtr errorPtr);
		public delegate void OnCloseCallback(int instanceId, int closeCode);

		private static bool isInitialized = false;
		
		private static void Initialize()
		{
			WebSocketSetOnOpen(DelegateOnOpenEvent);
			WebSocketSetOnMessage(DelegateOnMessageEvent);
			WebSocketSetOnError(DelegateOnErrorEvent);
			WebSocketSetOnClose(DelegateOnCloseEvent);

			isInitialized = true;
		}

		public static WebSocket CreateInstance(string url)
		{
			if (!isInitialized)
				Initialize();

			int instanceId = WebSocketAllocate(url);
			WebSocket wrapper = new WebSocket(instanceId);
			instances.Add(instanceId, wrapper);

			return wrapper;
		}

		internal WebSocket(int instanceId)
		{
			this.instanceId = instanceId;
		}

		~WebSocket()
		{
			HandleInstanceDestroy(this.instanceId);
		}

		public int GetInstanceId()
		{
			return this.instanceId;
		}

		public void Connect()
		{
			int ret = WebSocketConnect(this.instanceId);

			if (ret < 0)
				throw GetErrorMessageFromCode(ret, null);
		}

		
		public void Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
		{
			int ret = WebSocketClose(this.instanceId, (int)code, reason);

			if (ret < 0)
				throw GetErrorMessageFromCode(ret, null);
		}

		public void Send(byte[] data)
		{
			int ret = WebSocketSend(this.instanceId, data, data.Length);

			if (ret < 0)
				throw GetErrorMessageFromCode(ret, null);
		}

		public WebSocketState GetState()
		{
			int state = WebSocketGetState(this.instanceId);

			if (state < 0)
				throw GetErrorMessageFromCode(state, null);

			switch (state)
			{
				case 0:
					return WebSocketState.Connecting;

				case 1:
					return WebSocketState.Open;

				case 2:
					return WebSocketState.Closing;

				case 3:
					return WebSocketState.Closed;

				default:
					return WebSocketState.Closed;
			}

		}

		/// <summary>
		/// Delegates onOpen event from JSLIB to native sharp event
		/// Is called by WebSocketFactory
		/// </summary>
		/// RaiseOnOpenEvent
		public void DelegateOnOpenEvent()
		{
			OnOpen?.Invoke();
		}

		/// <summary>
		/// Delegates onMessage event from JSLIB to native sharp event
		/// Is called by WebSocketFactory
		/// </summary>
		/// <param name="data">Binary data.</param>
		public void DelegateOnMessageEvent(byte[] data)
		{
			OnMessage?.Invoke(data);
		}

		public void DelegateOnErrorEvent(string errorMsg)
		{
			OnError?.Invoke(errorMsg);
		}

		public void DelegateOnCloseEvent(int closeCode)
		{
			OnClose?.Invoke(ParseCloseCodeEnum(closeCode));
		}

		public static void HandleInstanceDestroy(int instanceId)
		{

			instances.Remove(instanceId);
			WebSocketFree(instanceId);
		}

		[MonoPInvokeCallback(typeof(OnOpenCallback))]
		public static void DelegateOnOpenEvent(int instanceId)
		{
			WebSocket instanceRef;

			if (instances.TryGetValue(instanceId, out instanceRef))
			{
				instanceRef.DelegateOnOpenEvent();
			}
		}

		[MonoPInvokeCallback(typeof(OnMessageCallback))]
		public static void DelegateOnMessageEvent(int instanceId, System.IntPtr msgPtr, int msgSize)
		{
			WebSocket instanceRef;

			if (instances.TryGetValue(instanceId, out instanceRef))
			{
				byte[] msg = new byte[msgSize];
				Marshal.Copy(msgPtr, msg, 0, msgSize);

				instanceRef.DelegateOnMessageEvent(msg);
			}
		}

		[MonoPInvokeCallback(typeof(OnErrorCallback))]
		public static void DelegateOnErrorEvent(int instanceId, System.IntPtr errorPtr)
		{
			WebSocket instanceRef;

			if (instances.TryGetValue(instanceId, out instanceRef))
			{
				string errorMsg = Marshal.PtrToStringAuto(errorPtr);
				instanceRef.DelegateOnErrorEvent(errorMsg);
			}
		}

		[MonoPInvokeCallback(typeof(OnCloseCallback))]
		public static void DelegateOnCloseEvent(int instanceId, int closeCode)
		{
			WebSocket instanceRef;

			if (instances.TryGetValue(instanceId, out instanceRef))
			{
				instanceRef.DelegateOnCloseEvent(closeCode);
			}
		}

		public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
		{
			if (WebSocketCloseCode.IsDefined(typeof(WebSocketCloseCode), closeCode))
			{
				return (WebSocketCloseCode)closeCode;
			}
			else
			{
				return WebSocketCloseCode.Undefined;
			}
		}

		public static WebSocketException GetErrorMessageFromCode(int errorCode, Exception inner)
		{
			switch (errorCode)
			{
				case -1: return new WebSocketUnexpectedException("WebSocket instance not found.", inner);
				case -2: return new WebSocketInvalidStateException("WebSocket is already connected or in connecting state.", inner);
				case -3: return new WebSocketInvalidStateException("WebSocket is not connected.", inner);
				case -4: return new WebSocketInvalidStateException("WebSocket is already closing.", inner);
				case -5: return new WebSocketInvalidStateException("WebSocket is already closed.", inner);
				case -6: return new WebSocketInvalidStateException("WebSocket is not in open state.", inner);
				case -7: return new WebSocketInvalidArgumentException("Cannot close WebSocket. An invalid code was specified or reason is too long.", inner);
				default: return new WebSocketUnexpectedException("Unknown error.", inner);
			}
		}
	}

	#region Exceptions

	public class WebSocketException : Exception
	{
		public WebSocketException()
		{
		}

		public WebSocketException(string message)
			: base(message)
		{
		}

		public WebSocketException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}

	/// <summary>
	/// Web socket exception raised when an error was not expected, probably due to corrupted internal state.
	/// </summary>
	public class WebSocketUnexpectedException : WebSocketException
	{
		public WebSocketUnexpectedException() { }
		public WebSocketUnexpectedException(string message) : base(message) { }
		public WebSocketUnexpectedException(string message, Exception inner) : base(message, inner) { }
	}

	/// <summary>
	/// Invalid argument exception raised when bad arguments are passed to a method.
	/// </summary>
	public class WebSocketInvalidArgumentException : WebSocketException
	{
		public WebSocketInvalidArgumentException() { }
		public WebSocketInvalidArgumentException(string message) : base(message) { }
		public WebSocketInvalidArgumentException(string message, Exception inner) : base(message, inner) { }
	}

	/// <summary>
	/// Invalid state exception raised when trying to invoke action which cannot be done due to different then required state.
	/// </summary>
	public class WebSocketInvalidStateException : WebSocketException
	{
		public WebSocketInvalidStateException() { }
		public WebSocketInvalidStateException(string message) : base(message) { }
		public WebSocketInvalidStateException(string message, Exception inner) : base(message, inner) { }
	}

	#endregion
}

#endif
