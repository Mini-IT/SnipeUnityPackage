using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;
using MiniIT.Snipe.Logging;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class WebSocketTransport : Transport
	{
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int BAD_CONNECTION_PING_INTERVAL = 1000; // milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds
		private const int LOGIN_TIMEOUT = 10000;
		private readonly byte[] COMPRESSED_HEADER = new byte[] { 0xAA, 0xBB };
		private readonly byte[] BATCH_HEADER = new byte[] { 0xAA, 0xBC };

		private bool _heartbeatEnabled = true;

		public bool HeartbeatEnabled
		{
			get { return _heartbeatEnabled; }
			set
			{
				if (_heartbeatEnabled != value)
				{
					_heartbeatEnabled = value;
					if (!_heartbeatEnabled)
						StopHeartbeat();
					else if (_loggedIn)
						StartHeartbeat();
				}
			}
		}

		public override bool Started => _webSocket != null;
		public override bool Connected => _webSocket != null && _webSocket.Connected;
		public override bool ConnectionEstablished => _connected;

		private WebSocketWrapper _webSocket = null;
		private readonly object _lock = new object();

		private ConcurrentQueue<IDictionary<string, object>> _sendMessages;
		private ConcurrentQueue<IList<IDictionary<string, object>>> _batchMessages;

		private readonly AlterSemaphore _sendSignal = new AlterSemaphore(0, int.MaxValue);
		private CancellationTokenSource _sendLoopCancellation;

		private bool _connected;
		private bool _loggedIn;

		public bool BadConnection { get; private set; } = false;
		private int _checkConnectionGeneration = 0;

		private CancellationTokenSource _checkConnectionCancellation;
		private CancellationTokenSource _loginTimeoutCancellation;

		internal WebSocketTransport(TransportOptions options) : base(options)
		{
		}

		public override void Connect(string url, ushort port = 0)
		{
			if (string.IsNullOrEmpty(url))
			{
				_logger.LogWarning("WebSocket Connect - URL is empty");
				_connectionClosedHandler?.Invoke(this);
				return;
			}

			_logger.LogTrace("WebSocket Connect to " + url);

			_webSocket = new WebSocketFactory(_snipeOptions, _services).CreateWebSocket();

			Info = new TransportInfo()
			{
				Protocol = TransportProtocol.WebSocket,
				ClientImplementation = _webSocket.GetType().Name,
			};

			_webSocket.OnConnectionOpened += OnWebSocketConnected;
			_webSocket.OnConnectionClosed += OnWebSocketClosed;
			_webSocket.ProcessMessage += ProcessWebSocketMessage;

			AlterTask.RunAndForget(() =>
			{
				_analytics.ConnectionUrl = url;

				try
				{
					_webSocket.Connect(url);
				}
				catch (Exception e)
				{
					_logger.LogTrace("Failed to connect to {url} - {error}", url, e);
				}
			});
		}

		public override void Disconnect()
		{
			_connected = false;
			_loggedIn = false;

			lock (_lock)
			{
				StopLoginTimeout();
				StopSendLoop();
				StopHeartbeat();
				StopCheckConnection();

				var ws = _webSocket;
				if (ws != null)
				{
					_webSocket.OnConnectionOpened -= OnWebSocketConnected;
					_webSocket.OnConnectionClosed -= OnWebSocketClosed;
					_webSocket.ProcessMessage -= ProcessWebSocketMessage;
					_webSocket.Disconnect();
					_webSocket = null;
					ws.Dispose();
				}
			}
		}

		public void SetLoggedIn(bool value)
		{
			if (_loggedIn == value)
			{
				return;
			}

			_loggedIn = value;

			if (_loggedIn && _heartbeatEnabled)
			{
				StopLoginTimeout();
				StartHeartbeat();
			}
		}

		private void OnWebSocketConnected()
		{
			_connected = true;

			_logger.LogTrace("OnWebSocketConnected");

			_connectionOpenedHandler?.Invoke(this);
		}

		private void OnWebSocketClosed()
		{
			CloseConncetion("OnWebSocketClosed");
		}

		private void CloseConncetion(string reason)
		{
			_logger.LogTrace("CloseConncetion: " + reason);

			_loggedIn = false;

			Disconnect();
			_connectionClosedHandler?.Invoke(this);
		}

		public override void SendMessage(IDictionary<string, object> message)
		{
			if (_sendMessages == null)
			{
				StartSendLoop();
			}

			_sendMessages!.Enqueue(message);
			_sendSignal.Release();
		}

		public override void SendBatch(IList<IDictionary<string, object>> messages)
		{
			lock (_lock)
			{
				_batchMessages ??= new ConcurrentQueue<IList<IDictionary<string, object>>>();
				_batchMessages.Enqueue(messages);

				if (_sendMessages == null)
				{
					StartSendLoop();
				}
			}

			_sendSignal.Release();
		}

		private async UniTask DoSendRequest(IDictionary<string, object> message)
		{
			byte[] data = await SerializeMessage(message);

			lock (_lock)
			{
				_webSocket?.SendRequest(data);
			}

			if (_sendMessages != null && _sendMessages.IsEmpty && !message.SafeGetString("t").StartsWith("payment/"))
			{
				StartCheckConnection();
			}
		}

		private async UniTask DoSendBatch(IList<IDictionary<string, object>> messages)
		{
			byte[][] data = new byte[messages.Count][];
			int length = 2; // 2 bytes for batch header
			for (int i = 0; i < messages.Count; i++)
			{
				data[i] = await SerializeMessage(messages[i]);
				length += data[i].Length + 3; // 3 bytes for serialized message size
			}

			byte[] request = new byte[length]; // don't use ArrayPool<byte> here
			request[0] = BATCH_HEADER[0];
			request[1] = BATCH_HEADER[1];
			int offset = 2;
			for (int i = 0; i < data.Length; i++)
			{
				length = data[i].Length;
				BytesUtil.WriteInt3(request, offset, length);
				offset += 3;
				Array.ConstrainedCopy(data[i], 0, request, offset, length);
				offset += length;
			}

			_webSocket?.SendRequest(request);
		}

		// [Testable]
		internal async UniTask<byte[]> SerializeMessage(IDictionary<string, object> message)
		{
			byte[] result = null;

			bool semaphoreOccupied = false;
			try
			{
				await _messageSerializationSemaphore.WaitAsync();
				semaphoreOccupied = true;

				result = await AlterTask.Run(() => InternalSerializeMessage(message));
			}
			finally
			{
				if (semaphoreOccupied && !_disposed)
				{
					_messageSerializationSemaphore.Release();
				}
			}

			return result;
		}

		private byte[] InternalSerializeMessage(IDictionary<string, object> message)
		{
			byte[] result = null;

			var msgData = _messageSerializer.Serialize(message);

			if (_snipeOptions.CompressionEnabled && msgData.Length >= _snipeOptions.MinMessageBytesToCompress) // compression needed
			{
				_logger.LogTrace("compress message");
				//_logger.LogTrace("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

				byte[] compressed = _messageCompressor.Compress(msgData);

				//_logger.LogTrace("Compressed:   " + BitConverter.ToString(compressed.Array, compressed.Offset, compressed.Count));

				result = new byte[compressed.Length + 2];
				result[0] = COMPRESSED_HEADER[0];
				result[1] = COMPRESSED_HEADER[1];
				Array.ConstrainedCopy(compressed, 0, result, 2, compressed.Length);
			}
			else // compression not needed
			{
				result = new byte[msgData.Length];
				msgData.CopyTo(result);
			}

			return result;
		}

		private void ProcessWebSocketMessage(byte[] rawData)
		{
			if (rawData.Length < 2)
			{
				return;
			}

			StopCheckConnection();

			AlterTask.RunAndForget(() => ProcessMessage(rawData).Forget());
		}

		private async UniTaskVoid ProcessMessage(byte[] rawData)
		{
			_logger.LogTrace("ProcessWebSocketMessage"); //   " + BitConverter.ToString(raw_data, 0, raw_data.Length));

			IDictionary<string, object> message;

			bool semaphoreOccupied = false;
			try
			{
				await _messageProcessingSemaphore.WaitAsync();
				semaphoreOccupied = true;

				message = await AlterTask.Run(() => ReadMessage(rawData));
			}
			finally
			{
				if (semaphoreOccupied && !_disposed)
				{
					_messageProcessingSemaphore.Release();
				}
			}

			_messageReceivedHandler?.Invoke(message);

			if (_heartbeatEnabled)
			{
				ResetHeartbeatTimer();
			}
		}

		private IDictionary<string, object> ReadMessage(byte[] buffer)
		{
			bool compressed = (buffer[0] == COMPRESSED_HEADER[0] && buffer[1] == COMPRESSED_HEADER[1]);
			if (compressed)
			{
				var compressedData = new ArraySegment<byte>(buffer, 2, buffer.Length - 2);
				var decompressed = _messageCompressor.Decompress(compressedData);
				return MessagePackDeserializer.Parse(decompressed) as IDictionary<string, object>;
			}
			else // uncompressed
			{
				return MessagePackDeserializer.Parse(buffer) as IDictionary<string, object>;
			}
		}

		#region Send loop

		private void StartSendLoop()
		{
			_logger.LogTrace("StartSendLoop");

			lock (_lock)
			{
				if (_sendLoopCancellation != null)
				{
					return;
				}

#if NET5_0_OR_GREATER
				if (_sendMessages == null)
					_sendMessages = new ConcurrentQueue<IDictionary<string, object>>();
				else
					_sendMessages.Clear();
#else
				_sendMessages = new ConcurrentQueue<IDictionary<string, object>>();
#endif

				CancellationTokenHelper.CancelAndDispose(ref _sendLoopCancellation);
				_sendLoopCancellation = new CancellationTokenSource();

				AlterTask.RunAndForget(() => SendLoop(_sendLoopCancellation.Token).Forget());
			}
		}

		private void StopSendLoop()
		{
			_logger.LogTrace("StopSendLoop");

			StopCheckConnection();

			lock (_lock)
			{
				if (_sendLoopCancellation != null)
				{
					_sendLoopCancellation.Cancel();
					_sendSignal.Release(); // wake waiter if it's currently blocked
				}

				CancellationTokenHelper.Dispose(ref _sendLoopCancellation, false);
			}

			_sendMessages = null;
			_batchMessages = null;
		}

		private async UniTaskVoid SendLoop(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested && Connected)
			{
				try
				{
					// Wait for signal or cancellation
					await _sendSignal.WaitAsync(cancellation).ConfigureAwait(false);

					if (cancellation.IsCancellationRequested)
					{
						break;
					}

					// Process all queued items (may be more than one)
					// Process batch messages first
					while (_batchMessages != null && !_batchMessages.IsEmpty && _batchMessages.TryDequeue(out var messages) &&
					       messages != null && messages.Count > 0)
					{
						await DoSendBatch(messages);
					}

					// Process single messages
					while (_sendMessages != null && !_sendMessages.IsEmpty && _sendMessages.TryDequeue(out var message) && message != null)
					{
						await DoSendRequest(message);
					}
				}
				catch (OperationCanceledException)
				{
					// This is OK. Just terminating the task
					return;
				}
				catch (Exception ex)
				{
					var e = ex is AggregateException ae ? ae.InnerException : ex;
					string exceptionMessage = LogUtil.GetReducedException(ex);
					_logger.LogTrace($"SendLoop Exception: {exceptionMessage}");
					_analytics.TrackError("WebSocket SendLoop error", e);

					StopSendLoop();
					return;
				}
			}
		}

		#endregion // Send loop

		#region Login timeout

		private void StartLoginTimeout()
		{
			StopLoginTimeout();

			_loginTimeoutCancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => LoginTimeoutTask(_loginTimeoutCancellation.Token).Forget());
		}

		private void StopLoginTimeout()
		{
			CancellationTokenHelper.CancelAndDispose(ref _loginTimeoutCancellation);
		}

		private async UniTaskVoid LoginTimeoutTask(CancellationToken cancellation)
		{
			try
			{
				await AlterTask.Delay(LOGIN_TIMEOUT, cancellation);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (cancellation.IsCancellationRequested)
			{
				return;
			}

			// If not logged in within timeout, consider the connection attempt failed and close transport.
			if (!_loggedIn && Started)
			{
				_logger.LogTrace($"LoginTimeoutTask - login timeout ({LOGIN_TIMEOUT} ms)");
				CloseConncetion($"Login timeout ({LOGIN_TIMEOUT} ms)");
			}
		}

		#endregion

		#region Heartbeat

		private long _heartbeatTriggerTicks = 0;

		private CancellationTokenSource _heartbeatCancellation;

		private void StartHeartbeat()
		{
			CancellationTokenHelper.CancelAndDispose(ref _heartbeatCancellation);

			// Custom heartbeating is needed only for WebSocketSharp
			if (_webSocket == null || _webSocket.AutoPing)
			{
				_heartbeatEnabled = false;
				return;
			}

			_heartbeatCancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => HeartbeatTask(_heartbeatCancellation.Token).Forget());
		}

		private void StopHeartbeat()
		{
			CancellationTokenHelper.CancelAndDispose(ref _heartbeatCancellation);
		}

		private async UniTaskVoid HeartbeatTask(CancellationToken cancellation)
		{
			_heartbeatTriggerTicks = 0;
			bool pinging = false;
			bool forcePing = false;
			var pingStopwatch = new Stopwatch();

			while (!cancellation.IsCancellationRequested && Connected)
			{
				// if ping was sent, but pong was not yet received within HEARTBEAT_TASK_DELAY,
				// then wait one more HEARTBEAT_TASK_DELAY and then send another ping
				if (pinging)
				{
					if (forcePing)
					{
						OnDisconnectDetected();
						break;
					}

					pinging = false;
					forcePing = true;
				}
				else if (forcePing || BadConnection || DateTime.UtcNow.Ticks >= _heartbeatTriggerTicks) // it's time to send a ping
				{
					lock (_lock)
					{
						pinging = true;
						pingStopwatch.Restart();

						_webSocket.Ping(pong =>
						{
							pinging = false;
							forcePing = false;
							pingStopwatch.Stop();
							var pingTime = pong ? pingStopwatch.Elapsed : TimeSpan.Zero;
							_analytics.PingTime = pingTime;

							if (pong)
							{
								_logger.LogTrace($"Heartbeat pong {pingTime.TotalMilliseconds} ms");
								if (BadConnection)
								{
									BadConnection = false;
									ResetHeartbeatTimer();
								}
							}
							else
							{
								_logger.LogTrace("Heartbeat pong NOT RECEIVED");
							}
						});
					}

					ResetHeartbeatTimer();

					_logger.LogTrace("Heartbeat ping");
				}

				if (cancellation.IsCancellationRequested)
				{
					break;
				}

				try
				{
					int delay = BadConnection ? BAD_CONNECTION_PING_INTERVAL : HEARTBEAT_TASK_DELAY;
					await AlterTask.Delay(delay, cancellation);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}

		private void ResetHeartbeatTimer()
		{
			_heartbeatTriggerTicks = DateTime.UtcNow.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		#endregion

		#region CheckConnection

		private void StartCheckConnection()
		{
			if (!_loggedIn)
			{
				return;
			}

			// _logger.LogTrace($"StartCheckConnection");

			if (_checkConnectionCancellation != null)
			{
				CancellationTokenHelper.CancelAndDispose(ref _checkConnectionCancellation);
			}

			_checkConnectionCancellation = new CancellationTokenSource();
			var cancellation = _checkConnectionCancellation.Token;
			int generation = Interlocked.Increment(ref _checkConnectionGeneration);
			AlterTask.RunAndForget(() => CheckConnectionTask(cancellation, generation).Forget());
		}

		private void StopCheckConnection()
		{
			if (_checkConnectionCancellation != null)
			{
				CancellationTokenHelper.CancelAndDispose(ref _checkConnectionCancellation);
			}

			Interlocked.Increment(ref _checkConnectionGeneration);
			BadConnection = false;
		}

		private async UniTaskVoid CheckConnectionTask(CancellationToken cancellation, int generation)
		{
			BadConnection = false;

			try
			{
				await AlterTask.Delay(CHECK_CONNECTION_TIMEOUT, cancellation);
			}
			catch (OperationCanceledException)
			{
				// This is OK. Just terminating the task
				return;
			}

			// if the connection is ok then this task should already be cancelled
			if (cancellation.IsCancellationRequested || generation != Volatile.Read(ref _checkConnectionGeneration))
			{
				return;
			}

			BadConnection = true;
			_logger.LogTrace("CheckConnectionTask - Bad connection detected");

			if (_heartbeatEnabled)
			{
				return;
			}

			bool pinging = false;
			while (Connected && BadConnection)
			{
				// if the connection is ok then this task should be cancelled
				if (cancellation.IsCancellationRequested || generation != Volatile.Read(ref _checkConnectionGeneration))
				{
					BadConnection = false;
					return;
				}

				if (pinging)
				{
					try
					{
						await AlterTask.Delay(BAD_CONNECTION_PING_INTERVAL, cancellation);
					}
					catch (OperationCanceledException)
					{
						return;
					}
				}
				else
				{
					lock (_lock)
					{
						pinging = true;
						_webSocket.Ping(pong =>
						{
							pinging = false;

							if (pong)
							{
								BadConnection = false;
								_logger.LogTrace("CheckConnectionTask - pong received");
							}
							else
							{
								_logger.LogTrace("CheckConnectionTask - pong NOT received");
								OnDisconnectDetected();
							}
						});
					}
				}
			}
		}

		private void OnDisconnectDetected()
		{
			if (Connected)
			{
				// Disconnect detected
				_logger.LogTrace("CheckConnectionTask - Disconnect detected");

				OnWebSocketClosed();
			}
		}

		#endregion
	}
}
