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
		// MsgPack serialized {"t":"server.ping"}
		private readonly byte[] PING_SERIALIZED_DATA = new byte[] { 0x81, 0xA1, 0x74, 0xAB, 0x73, 0x65, 0x72, 0x76, 0x65, 0x72, 0x2E, 0x70, 0x69, 0x6E, 0x67 };

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
				_webSocket = WebSocketPlugin.CreateInstance(url);
				_webSocket.OnOpen += OnWebSocketConnected;
				_webSocket.OnClose += OnWebSocketClosed;
				_webSocket.OnMessage += OnWebSocketMessage;
				_webSocket.OnError += OnWebSocketError;
			}

			_webSocket.Connect();
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
			if (!Connected)
			{
				callback?.Invoke(false);
				return;
			}

			SendRequest(PING_SERIALIZED_DATA);
		}

		protected override bool IsConnected()
		{
			return (_webSocket != null && _webSocket.GetState() == WebSocketState.Open);
		}

		public override void Dispose()
		{
			base.Dispose();
			_webSocket?.Dispose();
		}
	}

}

#endif
