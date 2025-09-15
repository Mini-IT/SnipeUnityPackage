#if UNITY_WEBGL && !UNITY_EDITOR
#define WEBGL_ENVIRONMENT
#endif

#if BEST_WEBSOCKET && !WEBGL_ENVIRONMENT

using System;
using System.Threading;
using System.Threading.Tasks;
using Best.HTTP.Shared.PlatformSupport.Memory;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;
using Best.WebSockets;

namespace MiniIT.Snipe
{
	public sealed class WebSocketBestWrapper : WebSocketWrapper
	{
		public override bool AutoPing => true;

		private WebSocket _webSocket = null;
		private CancellationTokenSource _connectionWaitingCancellation;
		private readonly object _lock = new object();
		private readonly ILogger _logger;

		public WebSocketBestWrapper()
		{
			_logger = SnipeServices.LogService.GetLogger(nameof(WebSocketBestWrapper));
			Best.HTTP.Shared.HTTPManager.Setup();
		}

		public override async void Connect(string url)
		{
			Disconnect();

			lock (_lock)
			{
				_webSocket = new WebSocket(new Uri(url));
				_webSocket.OnOpen += OnWebSocketConnected;
				_webSocket.OnClosed += OnWebSocketClosed;
				_webSocket.OnBinary += OnWebSocketMessage;
				_webSocket.SendPings = true;
				_webSocket.Open();;
			}

			_connectionWaitingCancellation = new CancellationTokenSource();
			await WaitForConnection(_connectionWaitingCancellation.Token);
			_connectionWaitingCancellation = null;
		}

		private async Task WaitForConnection(CancellationToken cancellation)
		{
			try
			{
				await Task.Delay(CONNECTION_TIMEOUT, cancellation);
			}
			catch (TaskCanceledException)
			{
				// This is OK. Just terminating the task
				return;
			}

			if (!cancellation.IsCancellationRequested && !Connected)
			{
				_logger.LogTrace("WaitForConnection - Connection timed out");
				OnWebSocketClosed(_webSocket, WebSocketStatusCodes.ClosedAbnormally, "WebSocketWrapper - Connection timed out");
			}
		}

		public override void Disconnect()
		{
			if (_connectionWaitingCancellation != null)
			{
				_connectionWaitingCancellation.Cancel();
				_connectionWaitingCancellation = null;
			}

			lock (_lock)
			{
				if (_webSocket != null)
				{
					_webSocket.OnOpen -= OnWebSocketConnected;
					_webSocket.OnClosed -= OnWebSocketClosed;
					_webSocket.OnBinary -= OnWebSocketMessage;
					_webSocket.Close();
					_webSocket = null;
				}
			}
		}

		protected void OnWebSocketConnected(WebSocket webSocket)
		{
			_connectionWaitingCancellation = null;

			OnConnectionOpened?.Invoke();
		}

		protected void OnWebSocketClosed(WebSocket webSocket, WebSocketStatusCodes code, string message)
		{
			_logger.LogTrace($"OnWebSocketClosed: {code} - {message}");

			Disconnect();

			OnConnectionClosed?.Invoke();
		}

		private void OnWebSocketMessage(WebSocket webSocket, BufferSegment data)
		{
			if (ProcessMessage == null)
			{
				return;
			}

			byte[] bytes = new byte[data.Count];
			Array.ConstrainedCopy(data.Data, data.Offset, bytes, 0, data.Count);

			ProcessMessage.Invoke(bytes);
		}

		public override void SendRequest(byte[] bytes)
		{
			if (!Connected)
				return;

			lock (_lock)
			{
				byte[] buffer = BufferPool.Get(bytes.Length, true);
				Array.ConstrainedCopy(bytes, 0, buffer, 0, bytes.Length);
				_webSocket.SendAsBinary(new BufferSegment(buffer, 0, bytes.Length));
			}
		}

		public override void SendRequest(ArraySegment<byte> data)
		{
			if (!Connected)
				return;

			lock (_lock)
			{
				byte[] buffer = BufferPool.Get(data.Count, true);
				Array.ConstrainedCopy(data.Array, data.Offset, buffer, 0, data.Count);
				_webSocket.SendAsBinary(new BufferSegment(buffer, 0, data.Count));
			}
		}

		public override void Ping(Action<bool> callback = null)
		{
			callback?.Invoke(false);
		}

		protected override bool IsConnected()
		{
			return (_webSocket != null && _webSocket.IsOpen);
		}
	}

}

#endif
