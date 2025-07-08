using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class WebSocketClientWrapper : WebSocketWrapper
	{
		public override bool AutoPing => true;

		private ClientWebSocket _webSocket = null;
		private CancellationTokenSource _cancellation;

		private readonly IMainThreadRunner _mainThreadRunner;

		private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim _readSemaphore = new SemaphoreSlim(1, 1);
		private readonly ArraySegment<byte> _receiveBuffer;

		private byte[] _receiveMessageBuffer;

		private readonly ConcurrentQueue<ArraySegment<byte>> _sendQueue = new ConcurrentQueue<ArraySegment<byte>>();
		private readonly ILogger _logger;

		/// <summary>
		/// <c>System.Net.WebSockets.ClientWebSocket</c> wrapper. Reads incoming messages by chunks
		/// of <para>receiveBufferSize</para> bytes and merges them into message buffer.
		/// </summary>
		/// <param name="receiveBufferSize">Max receive chunk size</param>
		/// <param name="messageBufferSize">Start receiving message buffer size. If the size of a message is greater than
		/// the size of the buffer, then the buffer will be automatically enlarged.</param>
		public WebSocketClientWrapper(int receiveBufferSize = 4096, int messageBufferSize = 10240)
		{
			_receiveBuffer = new ArraySegment<byte>(new byte[receiveBufferSize]);
			_receiveMessageBuffer = new byte[messageBufferSize];

			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_logger = SnipeServices.LogService.GetLogger(nameof(WebSocketClientWrapper));
		}

		public override void Connect(string url)
		{
			Disconnect();

			_cancellation = new CancellationTokenSource();
			_ = Task.Run(() => StartConnection(new Uri(url), _cancellation.Token));
		}

		private async Task StartConnection(Uri uri, CancellationToken cancellation)
		{
			_webSocket = new ClientWebSocket();

			try
			{
				var connectionCancellation = new CancellationTokenSource();
				connectionCancellation.CancelAfter(CONNECTION_TIMEOUT);

				Task connectionTask = _webSocket.ConnectAsync(uri, connectionCancellation.Token);
				await connectionTask;

				if (!connectionTask.IsCompleted)
				{
					_logger.LogTrace("WaitForConnection - Connection timed out");
					OnWebSocketClosed("WebSocketWrapper - Connection timed out");
					return;
				}
			}
			catch (Exception e)
			{
				string exceptionMessage = LogUtil.GetReducedException(e);
				_logger.LogTrace($"Connection failed: {exceptionMessage}");
				OnWebSocketClosed(e.ToString());
			}

			if (cancellation.IsCancellationRequested)
				return;

			if (_webSocket.State != WebSocketState.Open)
			{
				_logger.LogTrace("Connection failed");
				OnWebSocketClosed("Connection failed");
				return;
			}

			SendLoop(_webSocket, cancellation);
			ReceiveLoop(_webSocket, cancellation);

			OnWebSocketConnected();
		}

		public override void Disconnect()
		{
			Disconnect("Manual disconnect");
		}

		private void Disconnect(string reason)
		{
			CancellationTokenHelper.CancelAndDispose(ref _cancellation);

			if (_webSocket != null)
			{
				_webSocket.Dispose();
				_webSocket = null;
			}
		}

		private async void SendLoop(WebSocket webSocket, CancellationToken cancellation)
		{
			while (webSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
			{
				bool semaphoreOccupied = false;

				try
				{
					await _sendSemaphore.WaitAsync(cancellation);
					semaphoreOccupied = true;

					if (cancellation.IsCancellationRequested)
						break;

					if (_sendQueue.TryDequeue(out var sendData))
					{
						await webSocket.SendAsync(sendData, WebSocketMessageType.Binary, true, cancellation);
						ArrayPool<byte>.Shared.Return(sendData.Array);
					}
				}
				catch (WebSocketException e)
				{
					string exceptionMessage = LogUtil.GetReducedException(e);
					Disconnect($"Send exception: {exceptionMessage}");
				}
				catch (OperationCanceledException)
				{
					break;
				}
				finally
				{
					if (semaphoreOccupied)
					{
						_sendSemaphore.Release();
					}
				}

				await Task.Delay(50);
			}
		}

		private async void ReceiveLoop(WebSocket webSocket, CancellationToken cancellation)
		{
			int receivedMessageLength = 0;

			while (webSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
			{
				bool semaphoreOccupied = false;

				try
				{
					await _readSemaphore.WaitAsync(cancellation);
					semaphoreOccupied = true;

					if (cancellation.IsCancellationRequested)
						break;

					WebSocketReceiveResult result = await webSocket.ReceiveAsync(_receiveBuffer, cancellation);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server disconnected", cancellation);
						OnWebSocketClosed("Server disconnected");
						break;
					}

					if (cancellation.IsCancellationRequested)
					{
						break;
					}

					// data portion received
					int totalMessageLength = receivedMessageLength + result.Count;

					if (_receiveMessageBuffer.Length < totalMessageLength)
					{
						Array.Resize(ref _receiveMessageBuffer, totalMessageLength);
					}

					Array.ConstrainedCopy(_receiveBuffer.Array, _receiveBuffer.Offset, _receiveMessageBuffer, receivedMessageLength, result.Count);
					receivedMessageLength = totalMessageLength;

					if (result.EndOfMessage)
					{
						OnWebSocketMessage(new ArraySegment<byte>(_receiveMessageBuffer, 0, receivedMessageLength));
						receivedMessageLength = 0;
					}
				}
				catch (WebSocketException e)
				{
					string exceptionMessage = LogUtil.GetReducedException(e);
					Disconnect($"Receive exception: {exceptionMessage}");
				}
				catch (OperationCanceledException)
				{
					break;
				}
				finally
				{
					if (semaphoreOccupied)
					{
						_readSemaphore.Release();
					}
				}

				if (receivedMessageLength == 0)
				{
					await Task.Delay(50);
				}
			}
		}

		protected void OnWebSocketConnected()
		{
			if (OnConnectionOpened != null)
			{
				_mainThreadRunner.RunInMainThread(() => OnConnectionOpened?.Invoke());
			}
		}

		protected void OnWebSocketClosed(string reason)
		{
			_logger.LogTrace($"[WebSocketWrapper] OnWebSocketClosed: {reason}");

			Disconnect(reason);

			if (OnConnectionClosed != null)
			{
				_mainThreadRunner.RunInMainThread(() => OnConnectionClosed?.Invoke());
			}
		}

		private void OnWebSocketMessage(ArraySegment<byte> data)
		{
			byte[] bytes = new byte[data.Count];
			Array.ConstrainedCopy(data.Array, data.Offset, bytes, 0, data.Count);

			if (ProcessMessage != null)
			{
				_mainThreadRunner.RunInMainThread(() => ProcessMessage?.Invoke(bytes));
			}
		}

		public override void SendRequest(byte[] bytes)
		{
			if (!Connected)
				return;

			byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
			Array.ConstrainedCopy(bytes, 0, buffer, 0, bytes.Length);
			_sendQueue.Enqueue(new ArraySegment<byte>(buffer, 0, bytes.Length));
		}

		public override void SendRequest(ArraySegment<byte> data)
		{
			if (!Connected)
				return;

			byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Count);
			Array.ConstrainedCopy(data.Array, data.Offset, buffer, 0, data.Count);
			_sendQueue.Enqueue(new ArraySegment<byte>(buffer, 0, data.Count));
		}

		public override void Ping(Action<bool> callback = null)
		{
			callback?.Invoke(false);
		}

		protected override bool IsConnected()
		{
			return _webSocket != null && _webSocket.State == WebSocketState.Open;
		}

		public override void Dispose()
		{
			base.Dispose();
			_webSocket?.Dispose();

			CancellationTokenHelper.Dispose(ref _cancellation, false);
		}
	}

}
