#if UNITY_WEBGL && !UNITY_EDITOR

using System;

namespace WebSocketJS
{
	public class WebSocket : IDisposable
	{
		public delegate void WebSocketOpenEventHandler();
		public delegate void WebSocketMessageEventHandler(byte[] data);
		public delegate void WebSocketErrorEventHandler(string errorMsg);
		public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);

		public event WebSocketOpenEventHandler OnOpen;
		public event WebSocketMessageEventHandler OnMessage;
		public event WebSocketErrorEventHandler OnError;
		public event WebSocketCloseEventHandler OnClose;

		private readonly int _instanceId;

		/// <summary>
		/// Use <see cref="WebSocketPlugin.CreateInstance(string)"/>
		/// </summary>
		/// <param name="instanceId"></param>
		internal WebSocket(int instanceId)
		{
			_instanceId = instanceId;
		}

		~WebSocket()
		{
			WebSocketPlugin.DestroyInstance(_instanceId);
		}

		public void Dispose()
		{
			WebSocketPlugin.DestroyInstance(_instanceId);
			GC.SuppressFinalize(this);
		}

		public int GetInstanceId()
		{
			return _instanceId;
		}

		public void Connect()
		{
			int errorCode = WebSocketPlugin.WebSocketConnect(_instanceId);

			if (errorCode < 0)
			{
				throw GetExceptionFromCode(errorCode, null);
			}
		}
		
		public void Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
		{
			var state = GetState();
			if (state == WebSocketState.Closing || state == WebSocketState.Closed)
			{
				return;
			}

			int errorCode = WebSocketPlugin.WebSocketClose(_instanceId, (int)code, reason);

			if (errorCode < 0)
			{
				throw GetExceptionFromCode(errorCode, null);
			}
		}

		public void Send(byte[] data)
		{
			int errorCode = WebSocketPlugin.WebSocketSend(_instanceId, data, data.Length);

			if (errorCode < 0)
			{
				throw GetExceptionFromCode(errorCode, null);
			}
		}

		public WebSocketState GetState()
		{
			int state = WebSocketPlugin.WebSocketGetState(_instanceId);

			if (state < 0)
			{
				throw GetExceptionFromCode(state, null);
			}

			return state switch
			{
				0 => WebSocketState.Connecting,
				1 => WebSocketState.Open,
				2 => WebSocketState.Closing,
				3 => WebSocketState.Closed,
				_ => WebSocketState.Closed,
			};
		}

		internal void RaiseOnOpenEvent()
		{
			OnOpen?.Invoke();
		}

		internal void RaiseOnMessageEvent(byte[] data)
		{
			OnMessage?.Invoke(data);
		}

		internal void RaiseOnErrorEvent(string errorMsg)
		{
			OnError?.Invoke(errorMsg);
		}

		internal void RaiseOnCloseEvent(int closeCode)
		{
			WebSocketCloseCode code = Enum.IsDefined(typeof(WebSocketCloseCode), closeCode) ?
				(WebSocketCloseCode)closeCode :
				WebSocketCloseCode.Undefined;

			OnClose?.Invoke(code);
		}

		private static WebSocketException GetExceptionFromCode(int errorCode, Exception inner)
		{
			return errorCode switch
			{
				-1 => new WebSocketUnexpectedException("WebSocket instance not found", inner),
				-2 => new WebSocketInvalidStateException("WebSocket is already connected or trying to connect", inner),
				-3 => new WebSocketInvalidStateException("WebSocket is not connected", inner),
				-4 => new WebSocketInvalidStateException("WebSocket is already closing", inner),
				-5 => new WebSocketInvalidStateException("WebSocket is already closed", inner),
				-6 => new WebSocketInvalidStateException("WebSocket is not in open state", inner),
				-7 => new WebSocketInvalidArgumentException("Cannot close WebSocket. An invalid code was specified or reason is too long", inner),
				_ => new WebSocketUnexpectedException("Unknown error", inner),
			};
		}
	}
}

#endif
