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
		// MsgPack serialized {"t":"server.ping"}
		private readonly byte[] PING_SERIALIZED_DATA = new byte[] { 0x81, 0xA1, 0x74, 0xAB, 0x73, 0x65, 0x72, 0x76, 0x65, 0x72, 0x2E, 0x70, 0x69, 0x6E, 0x67 };
		private readonly byte[] PONG_FRAGMENT = new byte[] { 0xA1, 0x74, 0xAB, 0x73, 0x65, 0x72, 0x76, 0x65, 0x72, 0x2E, 0x70, 0x69, 0x6E, 0x67 }; // "t":"server.ping"
		//private readonly byte[] PONG_FRAGMENT_OK = new byte[] { 0xA9, 0x65, 0x72, 0x72, 0x6F, 0x72, 0x43, 0x6F, 0x64, 0x65, 0xA2, 0x6F, 0x6B }; // "errorCode":"ok"

		public override bool AutoPing => false;

		private ClientWebSocket _webSocket = null;
		private CancellationTokenSource _cancellation;

		private Action<bool> _pongCallback;
		private readonly object _pongLock = new object();

		private readonly IMainThreadRunner _mainThreadRunner;

		private readonly SemaphoreSlim _sendSignalSemaphore = new SemaphoreSlim(0, int.MaxValue);
		private readonly SemaphoreSlim _readSemaphore = new SemaphoreSlim(1, 1);
		private readonly ArraySegment<byte> _receiveBuffer;

		private byte[] _receiveMessageBuffer;

		private readonly ConcurrentQueue<ArraySegment<byte>> _sendQueue = new ConcurrentQueue<ArraySegment<byte>>();
		private readonly ILogger _logger;

		private int _closeNotified; // 0 = not closed, 1 = close already notified

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

			_mainThreadRunner = SnipeServices.Instance.MainThreadRunner;
			_logger = SnipeServices.Instance.LogService.GetLogger(nameof(WebSocketClientWrapper));
		}

		public override void Connect(string url)
		{
			Disconnect();

			_closeNotified = 0; // reset for new connection attempt

			_cancellation = new CancellationTokenSource();
			_ = Task.Run(() => StartConnection(new Uri(url), _cancellation.Token));
		}

		private async Task StartConnection(Uri uri, CancellationToken cancellation)
		{
			_webSocket = new ClientWebSocket();

			// Set keep-alive interval to detect dead connections
			// ClientWebSocket will automatically send ping frames at this interval
			_webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

			try
			{
				var connectionCancellation = new CancellationTokenSource();
				connectionCancellation.CancelAfter(CONNECTION_TIMEOUT);

				Task connectionTask = _webSocket.ConnectAsync(uri, connectionCancellation.Token);
				await connectionTask;

				if (!connectionTask.IsCompleted)
				{
					OnWebSocketClosed("WebSocketWrapper - Connection timed out");
					return;
				}
			}
			catch (OperationCanceledException)
			{
				OnWebSocketClosed("WebSocketWrapper - Connection timed out");
				return;
			}
			catch (Exception e)
			{
				string exceptionMessage = LogUtil.GetReducedException(e);
				OnWebSocketClosed("Connection failed: " + exceptionMessage);
				return;
			}

			if (cancellation.IsCancellationRequested)
			{
				return;
			}

			if (_webSocket.State != WebSocketState.Open)
			{
				_logger.LogTrace("Connection failed");
				OnWebSocketClosed("Connection failed");
				return;
			}

			_ = SendLoop(_webSocket, cancellation);
			_ = ReceiveLoop(_webSocket, cancellation);

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

		private async Task SendLoop(WebSocket webSocket, CancellationToken cancellation)
		{
			while (webSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
			{
				try
				{
					await _sendSignalSemaphore.WaitAsync(cancellation);

					if (cancellation.IsCancellationRequested)
						break;

					// Process all queued items (may be more than one)
					while (_sendQueue.TryDequeue(out var sendData))
					{
						await webSocket.SendAsync(sendData, WebSocketMessageType.Binary, true, cancellation);

						byte[] buffer = sendData.Array;
						if (!ReferenceEquals(buffer, PING_SERIALIZED_DATA))
						{
							ArrayPool<byte>.Shared.Return(buffer);
						}
					}
				}
				catch (WebSocketException e)
				{
					string exceptionMessage = LogUtil.GetReducedException(e);
					OnWebSocketClosed($"Send exception: {exceptionMessage}");
					break;
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}

		private async Task ReceiveLoop(WebSocket webSocket, CancellationToken cancellation)
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
				catch (OperationCanceledException)
				{
					break;
				}
				catch (WebSocketException e)
				{
					string exceptionMessage = LogUtil.GetReducedException(e);
					OnWebSocketClosed($"Receive exception: {exceptionMessage}");
				}
				catch (System.Net.Sockets.SocketException e)
				{
					// Network errors (e.g., network unreachable, connection reset)
					string exceptionMessage = LogUtil.GetReducedException(e);
					_logger.LogTrace($"ReceiveLoop - SocketException: {exceptionMessage}");
					OnWebSocketClosed($"Network error: {exceptionMessage}");
					break;
				}
				catch (Exception e)
				{
					// Catch any other exceptions that might indicate connection issues
					string exceptionMessage = LogUtil.GetReducedException(e);
					_logger.LogTrace($"ReceiveLoop - Unexpected exception: {exceptionMessage}");

					// Check if WebSocket state is still Open - if not, connection is dead
					if (webSocket.State != WebSocketState.Open)
					{
						OnWebSocketClosed($"Connection lost: {exceptionMessage}");
						break;
					}

					// If state is still Open but we got an exception, it might be transient
					// Log it but don't disconnect immediately - let the next iteration handle it
				}
				finally
				{
					if (semaphoreOccupied)
					{
						_readSemaphore.Release();
					}
				}
			}
		}

		private void OnWebSocketConnected()
		{
			if (OnConnectionOpened != null)
			{
				_mainThreadRunner.RunInMainThread(() => OnConnectionOpened?.Invoke());
			}
		}

		private void OnWebSocketClosed(string reason)
		{
			// Thread-safe guard from firing twice
			if (Interlocked.Exchange(ref _closeNotified, 1) != 0)
			{
				return;
			}

			_logger.LogTrace($"[WebSocketWrapper] OnWebSocketClosed: {reason}");

			Disconnect(reason);

			if (OnConnectionClosed != null)
			{
				_mainThreadRunner.RunInMainThread(() => OnConnectionClosed?.Invoke());
			}
		}

		private void OnWebSocketMessage(ArraySegment<byte> data)
		{
			// Check if this is a custom PONG message
			if (data.AsSpan().IndexOf(PONG_FRAGMENT) >= 0)
			{
				lock (_pongLock)
				{
					if (_pongCallback != null)
					{
						_pongCallback.Invoke(true);
						_pongCallback = null;
					}
				}
				return;
			}

			if (ProcessMessage == null)
			{
				return;
			}

			byte[] bytes = new byte[data.Count];
			Array.ConstrainedCopy(data.Array, data.Offset, bytes, 0, data.Count);

			_mainThreadRunner.RunInMainThread(() => ProcessMessage?.Invoke(bytes));
		}

		public override void SendRequest(byte[] bytes)
		{
			if (!Connected)
			{
				return;
			}

			byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
			Array.ConstrainedCopy(bytes, 0, buffer, 0, bytes.Length);
			_sendQueue.Enqueue(new ArraySegment<byte>(buffer, 0, bytes.Length));
			_sendSignalSemaphore.Release();
		}

		public override void SendRequest(ArraySegment<byte> data)
		{
			if (!Connected)
			{
				return;
			}

			byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Count);
			Array.ConstrainedCopy(data.Array, data.Offset, buffer, 0, data.Count);
			_sendQueue.Enqueue(new ArraySegment<byte>(buffer, 0, data.Count));
			_sendSignalSemaphore.Release();
		}

		public override void Ping(Action<bool> callback = null)
		{
			lock (_pongLock)
			{
				if (!Connected)
				{
					if (_pongCallback != callback)
					{
						_pongCallback?.Invoke(false);
					}
					_pongCallback = null;

					callback?.Invoke(false);
					return;
				}

				_pongCallback = callback;
			}

			_sendQueue.Enqueue(PING_SERIALIZED_DATA);
			_sendSignalSemaphore.Release();
		}

		protected override bool IsConnected()
		{
			return _webSocket != null && _webSocket.State == WebSocketState.Open;
		}

		public override void Dispose()
		{
			base.Dispose();
			_webSocket?.Dispose();
			_sendSignalSemaphore?.Dispose();
			_readSemaphore?.Dispose();

			CancellationTokenHelper.Dispose(ref _cancellation, false);
		}
	}

}
