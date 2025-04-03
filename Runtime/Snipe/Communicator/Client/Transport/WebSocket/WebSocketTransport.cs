#if UNITY_WEBGL && !UNITY_EDITOR
#define WEBGL_ENVIRONMENT
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;
using MiniIT.Threading;
using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe
{
	public class WebSocketTransport : Transport
	{
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds
		private readonly byte[] COMPRESSED_HEADER = new byte[] { 0xAA, 0xBB };
		private readonly byte[] BATCH_HEADER = new byte[] { 0xAA, 0xBC };

		protected bool _heartbeatEnabled = true;
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

		private bool _connected;
		private bool _loggedIn;

		internal WebSocketTransport(SnipeConfig config, SnipeAnalyticsTracker analytics)
			: base(config, analytics)
		{
		}

		public override void Connect()
		{
			string url = _config.GetWebSocketUrl();

			_logger.LogTrace("WebSocket Connect to " + url);

#if WEBGL_ENVIRONMENT
			_webSocket = new WebSocketJSWrapper();
#else
			_webSocket = _config.WebSocketImplementation switch
			{
				WebSocketImplementations.ClientWebSocket => new WebSocketClientWrapper(),
				WebSocketImplementations.BestWebSocket => new WebSocketBestWrapper(),
				_ => new WebSocketSharpWrapper(),
			};
#endif

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
				StopSendTask();
				StopHeartbeat();
				StopCheckConnection();

				if (_webSocket != null)
				{
					var ws = _webSocket;
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
				return;

			_loggedIn = value;

			if (_loggedIn && _heartbeatEnabled)
			{
				StartHeartbeat();
			}
		}

		private void OnWebSocketConnected()
		{
			_connected = true;

			_logger.LogTrace("OnWebSocketConnected");

			ConnectionOpenedHandler?.Invoke(this);
		}

		protected void OnWebSocketClosed()
		{
			_logger.LogTrace("OnWebSocketClosed");

			_loggedIn = false;

			if (!_connected) // failed to establish connection
			{
				_config.NextWebSocketUrl();
			}

			ConnectionClosedHandler?.Invoke(this);
		}

		public override void SendMessage(IDictionary<string, object> message)
		{
			if (_sendMessages == null)
			{
				StartSendTask();
			}
			_sendMessages!.Enqueue(message);
		}

		public override void SendBatch(IList<IDictionary<string, object>> messages)
		{
			lock (_lock)
			{
				_batchMessages ??= new ConcurrentQueue<IList<IDictionary<string, object>>>();
				_batchMessages.Enqueue(messages);

				if (_sendMessages == null)
				{
					StartSendTask();
				}
			}
		}

		private async void DoSendRequest(IDictionary<string, object> message)
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

		private async void DoSendBatch(IList<IDictionary<string, object>> messages)
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
				if (semaphoreOccupied)
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

			if (_config.CompressionEnabled && msgData.Length >= _config.MinMessageBytesToCompress) // compression needed
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

		protected void ProcessWebSocketMessage(byte[] rawData)
		{
			if (rawData.Length < 2)
				return;

			StopCheckConnection();

			ProcessMessage(rawData);
		}

		private async void ProcessMessage(byte[] rawData)
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
				if (semaphoreOccupied)
				{
					_messageProcessingSemaphore.Release();
				}
			}

			MessageReceivedHandler?.Invoke(message);

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

		#region Send task

		private CancellationTokenSource _sendTaskCancellation;

		private void StartSendTask()
		{
			_sendTaskCancellation?.Cancel();

#if NET5_0_OR_GREATER
			if (_sendMessages == null)
				_sendMessages = new ConcurrentQueue<IDictionary<string, object>>();
			else
				_sendMessages.Clear();
#else
			_sendMessages = new ConcurrentQueue<IDictionary<string, object>>();
#endif

			_sendTaskCancellation = new CancellationTokenSource();

			AlterTask.RunAndForget(() => SendTask(_sendTaskCancellation?.Token));
		}

		private void StopSendTask()
		{
			StopCheckConnection();

			if (_sendTaskCancellation != null)
			{
				_sendTaskCancellation.Cancel();
				_sendTaskCancellation.Dispose();
				_sendTaskCancellation = null;
			}

			_sendMessages = null;
		}

		private async void SendTask(CancellationToken? cancellation)
		{
			try
			{
				while (cancellation?.IsCancellationRequested != true && Connected)
				{
					if (_batchMessages != null && !_batchMessages.IsEmpty && _batchMessages.TryDequeue(out var messages) && messages != null && messages.Count > 0)
					{
						DoSendBatch(messages);
					}

					if (_sendMessages != null && !_sendMessages.IsEmpty && _sendMessages.TryDequeue(out var message) && message != null)
					{
						DoSendRequest(message);
					}

					await AlterTask.Delay(100);
				}
			}
			catch (Exception task_exception)
			{
				var e = task_exception is AggregateException ae ? ae.InnerException : task_exception;
				_logger.LogTrace($"SendTask Exception: {e}");
				_analytics.TrackError("WebSocket SendTask error", e);

				StopSendTask();
			}
		}

		#endregion // Send task

		#region Heartbeat

		private long _heartbeatTriggerTicks = 0;

		private CancellationTokenSource _heartbeatCancellation;

		private void StartHeartbeat()
		{
			_heartbeatCancellation?.Cancel();

			// Custom heartbeating is needed only for WebSocketSharp
			if (_webSocket == null && _webSocket.AutoPing)
			{
				_heartbeatEnabled = false;
				return;
			}

			_heartbeatCancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => HeartbeatTask(_heartbeatCancellation.Token));
		}

		private void StopHeartbeat()
		{
			if (_heartbeatCancellation != null)
			{
				_heartbeatCancellation.Cancel();
				_heartbeatCancellation = null;
			}
		}

		private async void HeartbeatTask(CancellationToken cancellation)
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
					pinging = false;
					forcePing = true;
				}
				else if (forcePing || DateTime.UtcNow.Ticks >= _heartbeatTriggerTicks) // it's time to send a ping
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
							_analytics.PingTime = pong ? pingStopwatch.Elapsed : TimeSpan.Zero;

							if (pong)
							{
								_logger.LogTrace($"Heartbeat pong {_analytics.PingTime.TotalMilliseconds} ms");
							}
							else
							{
								_logger.LogTrace($"Heartbeat pong NOT RECEIVED");
							}
						});
					}

					ResetHeartbeatTimer();

					_logger.LogTrace($"Heartbeat ping");
				}

				if (cancellation.IsCancellationRequested)
				{
					break;
				}

				try
				{
					await AlterTask.Delay(HEARTBEAT_TASK_DELAY, cancellation);
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

		public bool BadConnection { get; private set; } = false;

		private CancellationTokenSource _checkConnectionCancellation;

		private void StartCheckConnection()
		{
			if (!_loggedIn)
				return;

			// _logger.LogTrace($"StartCheckConnection");

			_checkConnectionCancellation?.Cancel();

			_checkConnectionCancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => CheckConnectionTask(_checkConnectionCancellation.Token));
		}

		private void StopCheckConnection()
		{
			if (_checkConnectionCancellation != null)
			{
				_checkConnectionCancellation.Cancel();
				_checkConnectionCancellation = null;

				// _logger.LogTrace($"StopCheckConnection");
			}

			BadConnection = false;
		}

		private async void CheckConnectionTask(CancellationToken cancellation)
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
			if (cancellation.IsCancellationRequested)
				return;

			BadConnection = true;
			_logger.LogTrace($"CheckConnectionTask - Bad connection detected");

			bool pinging = false;
			while (Connected && BadConnection)
			{
				// if the connection is ok then this task should be cancelled
				if (cancellation.IsCancellationRequested)
				{
					BadConnection = false;
					return;
				}

				if (pinging)
				{
					await AlterTask.Delay(100);
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
								_logger.LogTrace($"CheckConnectionTask - pong received");
							}
							else
							{
								_logger.LogTrace($"CheckConnectionTask - pong NOT received");
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
				_logger.LogTrace($"CheckConnectionTask - Disconnect detected");

				OnWebSocketClosed();
			}
		}

		#endregion
	}
}
