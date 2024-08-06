#if UNITY_WEBGL && !UNITY_EDITOR
#define WEBGL_ENVIRONMENT
#endif

#if WEBGL_ENVIRONMENT

using System;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;
using WebSocketJS;

namespace MiniIT.Snipe
{
	public class WebSocketJSWrapper : WebSocketWrapper
	{
		public override bool AutoPing => false;

		private WebSocket _webSocket = null;
		private readonly object _lock = new object();
		private readonly ILogger _logger;

		public WebSocketJSWrapper()
		{
			_logger = SnipeServices.LogService.GetLogger(nameof(WebSocketJSWrapper));
		}

		public override void Connect(string url)
		{
			Disconnect();

			lock (_lock)
			{
				_webSocket = WebSocket.CreateInstance(url);
				_webSocket.OnOpen += OnWebSocketConnected;
				_webSocket.OnClose += OnWebSocketClosed;
				_webSocket.OnMessage += OnWebSocketMessage;
				_webSocket.OnError += OnWebSocketError;
			}
		}

		public override void Disconnect()
		{
			lock (_lock)
			{
				if (_webSocket != null)
				{
					_webSocket.OnOpen -= OnWebSocketConnected;
					_webSocket.OnClose -= OnWebSocketClosed;
					_webSocket.OnMessage -= OnWebSocketMessage;
					_webSocket.Close();
					_webSocket = null;
				}
			}
		}

		protected void OnWebSocketConnected()
		{
			OnConnectionOpened?.Invoke();
		}

		protected void OnWebSocketClosed(WebSocketCloseCode code)
		{
			_logger.LogTrace($"OnWebSocketClosed: code = {code}");

			Disconnect();

			OnConnectionClosed?.Invoke();
		}

		private void OnWebSocketMessage(byte[] msg)
		{
			ProcessMessage?.Invoke(msg);
		}

		protected void OnWebSocketError(string errMsg)
		{
			_logger.LogTrace($"OnWebSocketError: {errMsg}");
		}

		public override void SendRequest(byte[] bytes)
		{
			if (!Connected)
				return;

			lock (_lock)
			{
				_webSocket.Send(bytes);
			}
		}

		public override void SendRequest(ArraySegment<byte> data)
		{
			if (!Connected)
				return;

			lock (_lock)
			{
				var bytes = new byte[data.Count];
				Array.ConstrainedCopy(data.Array, data.Offset, bytes, 0, data.Count);
				_webSocket.Send(bytes);
			}
		}

		public override void Ping(Action<bool> callback = null)
		{
			callback?.Invoke(false);
		}

		protected override bool IsConnected()
		{
			return (_webSocket != null && _webSocket.GetState() == WebSocketState.Open);
		}
	}

}

#endif
