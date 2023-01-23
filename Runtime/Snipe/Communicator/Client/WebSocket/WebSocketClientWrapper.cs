using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Buffers;
using System.Collections.Concurrent;

namespace MiniIT.Snipe
{
	public class WebSocketClientWrapper : WebSocketWrapper
	{
		private ClientWebSocket _webSocket = null;
		private CancellationTokenSource _cancellation;

		private TaskScheduler _taskScheduler;

		private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1);
		private readonly SemaphoreSlim _readSemaphore = new SemaphoreSlim(1);
		private readonly ArraySegment<byte> _receiveBuffer;

		private byte[] _receiveMessageBuffer;

		private ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();

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
			_taskScheduler = (SynchronizationContext.Current != null) ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;
		}

		public override void Connect(string url)
		{
			Disconnect();
			
			Analytics.TrackSocketStartConnection("WebSocketClientWrapper");

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
					DebugLogger.Log("[WebSocketClientWrapper] WaitForConnection - Connection timed out");
					OnWebSocketClosed("WebSocketWrapper - Connection timed out");
					return;
				}
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[WebSocketClientWrapper] Connection failed: {e}");
				OnWebSocketClosed(e.ToString());
			}

			if (cancellation.IsCancellationRequested)
				return;

			if (_webSocket.State != WebSocketState.Open)
			{
				DebugLogger.Log("[WebSocketClientWrapper] Connection failed");
				OnWebSocketClosed("Connection failed");
				return;
			}

			SendLoop(_webSocket, cancellation);
			ReceiveLoop(_webSocket, cancellation);

			OnWebSocketConnected();
		}

		public override void Disconnect()
		{
			if (_cancellation != null)
			{
				_cancellation.Cancel();
				_cancellation = null;
			}
			
			if (_webSocket != null)
			{
				// SetConnectionAnalyticsValues();
				Analytics.WebSocketDisconnectReason = "Manual disconnect";

				_webSocket.Dispose();
				_webSocket = null;
			}
		}

		private async void SendLoop(WebSocket webSocket, CancellationToken cancellation)
		{
			while (webSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
			{
				try
				{
					await _sendSemaphore.WaitAsync(cancellation);

					if (cancellation.IsCancellationRequested)
						break;

					if (_sendQueue.TryDequeue(out byte[] sendData))
					{
						await webSocket.SendAsync(new ArraySegment<byte>(sendData), WebSocketMessageType.Binary, true, cancellation);
						ArrayPool<byte>.Shared.Return(sendData);
					}
				}
				finally
				{
					_sendSemaphore.Release();
				}

				await Task.Delay(50);
			}
		}

		private async void ReceiveLoop(WebSocket webSocket, CancellationToken cancellation)
		{
			int receivedMessageLength = 0;

			while (webSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
			{
				try
				{
					await _readSemaphore.WaitAsync(cancellation);

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
						break;

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
				finally
				{
					_readSemaphore.Release();
				}

				if (receivedMessageLength == 0)
				{
					await Task.Delay(50);
				}
			}
		}

		protected void OnWebSocketConnected()
		{
			// SetConnectionAnalyticsValues();
			Analytics.WebSocketDisconnectReason = null;

			new Task(() => OnConnectionOpened?.Invoke()).RunSynchronously(_taskScheduler);
		}

		protected void OnWebSocketClosed(string reason)
		{
			DebugLogger.Log($"[WebSocketWrapper] OnWebSocketClosed: {reason}");
			
			Disconnect();
			Analytics.WebSocketDisconnectReason = reason;

			new Task(() => OnConnectionClosed?.Invoke()).RunSynchronously(_taskScheduler);
		}

		private void OnWebSocketMessage(ArraySegment<byte> data)
		{
			byte[] bytes = new byte[data.Count];
			Array.ConstrainedCopy(data.Array, data.Offset, bytes, 0, data.Count);

			new Task(() => ProcessMessage?.Invoke(bytes)).RunSynchronously(_taskScheduler);
		}
		
		public override void SendRequest(byte[] bytes)
		{
			if (!Connected)
				return;

			EnqueueRequestToSend(new ArraySegment<byte>(bytes));
		}

		public override void SendRequest(ArraySegment<byte> data)
		{
			if (!Connected)
				return;

			EnqueueRequestToSend(data);
		}

		private void EnqueueRequestToSend(ArraySegment<byte> data)
		{
			byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Count);
			Array.ConstrainedCopy(data.Array, data.Offset, buffer, 0, data.Count);
			_sendQueue.Enqueue(buffer);
		}

		public override void Ping(Action<bool> callback = null)
		{
			callback?.Invoke(false);
		}

		protected override bool IsConnected()
		{
			return _webSocket != null && _webSocket.State == WebSocketState.Open;
		}
	}

}