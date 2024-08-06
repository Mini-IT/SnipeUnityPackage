#if UNITY_WEBGL && !UNITY_EDITOR
#define WEBGL_ENVIRONMENT
#endif

#if !WEBGL_ENVIRONMENT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;
using WebSocketSharp;

namespace MiniIT.Snipe
{
	public class WebSocketSharpWrapper : WebSocketWrapper
	{
		public override bool AutoPing => false;

		private WebSocket _webSocket = null;
		private CancellationTokenSource _connectionWaitingCancellation;
		private readonly object _lock = new object();
		private readonly ILogger _logger;

		public WebSocketSharpWrapper()
		{
			_logger = SnipeServices.LogService.GetLogger(nameof(WebSocketSharpWrapper));
		}

		public override async void Connect(string url)
		{
			Disconnect();

			lock (_lock)
			{
				_webSocket = new WebSocket(url);
				_webSocket.OnOpen += OnWebSocketConnected;
				_webSocket.OnClose += OnWebSocketClosed;
				_webSocket.OnMessage += OnWebSocketMessage;
				_webSocket.OnError += OnWebSocketError;
				_webSocket.NoDelay = true;
				//_webSocket.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
				_webSocket.ConnectAsync();
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
				OnWebSocketClosed(this, new CloseEventArgs(0, "WebSocketWrapper - Connection timed out", false));
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
					_webSocket.OnClose -= OnWebSocketClosed;
					_webSocket.OnMessage -= OnWebSocketMessage;
					_webSocket.CloseAsync();
					_webSocket = null;
				}
			}
		}

		protected void OnWebSocketConnected(object sender, EventArgs e)
		{
			_connectionWaitingCancellation = null;

			OnConnectionOpened?.Invoke();
		}

		protected void OnWebSocketClosed(object sender, CloseEventArgs e)
		{
			_logger.LogTrace($"OnWebSocketClosed: {e?.Reason}");
			
			Disconnect();

			OnConnectionClosed?.Invoke();
		}

		private void OnWebSocketMessage(object sender, MessageEventArgs e)
		{
			ProcessMessage?.Invoke(e.RawData);
		}
		
		protected void OnWebSocketError(object sender, ErrorEventArgs e)
		{
			_logger.LogTrace($"OnWebSocketError: {e}");
			//Analytics.TrackError($"WebSocketError: {e.Message}", e.Exception);
		}
		
		//public override void SendRequest(string message)
		//{
		//	if (!Connected)
		//		return;

		//	lock (_lock)
		//	{
		//		_webSocket.SendAsync(message, null);
		//	}
		//}

		public override void SendRequest(byte[] bytes)
		{
			if (!Connected)
				return;

			lock (_lock)
			{
				_webSocket.SendAsync(bytes, null);
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
				_webSocket.SendAsync(bytes, null);
			}
		}

		public override void Ping(Action<bool> callback = null)
		{
			if (!Connected)
			{
				callback?.Invoke(false);
				return;
			}

			_webSocket.PingAsync(callback);
		}

		protected override bool IsConnected()
		{
			return (_webSocket != null && _webSocket.ReadyState == WebSocketState.Open);
		}
	}

}

#endif