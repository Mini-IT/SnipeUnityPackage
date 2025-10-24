using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class KcpTransport : Transport
	{
		public override bool Started => _kcpConnection != null;
		public override bool Connected => _kcpConnection != null && _kcpConnection.Connected;
		public override bool ConnectionEstablished => _connectionEstablished;
		public override bool ConnectionVerified => _connectionVerified;

		private KcpConnection _kcpConnection;
		private CancellationTokenSource _networkLoopCancellation;

		private bool _connectionEstablished = false;
		private bool _connectionVerified = false;
		private readonly object _lock = new object();

		internal KcpTransport(SnipeConfig config, SnipeAnalyticsTracker analytics)
			: base(config, analytics)
		{
			Info = new TransportInfo()
			{
				Protocol = TransportProtocol.Kcp,
				ClientImplementation = "kcp"
			};
		}

		public override void Connect()
		{
			lock (_lock)
			{
				if (_kcpConnection != null) // already connected or trying to connect
					return;

				_connectionEstablished = false;

				_kcpConnection = new KcpConnection
				{
					OnAuthenticated = OnClientConnected,
					OnData = OnClientDataReceived,
					OnDisconnected = OnClientDisconnected
				};
			}

			Task.Run(() =>
			{
				lock (_lock)
				{
					var address = _config.GetUdpAddress();
					string url = $"{address.Host}:{address.Port}";
					_analytics.ConnectionUrl = url;

					try
					{
						_kcpConnection.Connect(address.Host, address.Port, 3000, 5000);
					}
					catch (Exception e)
					{
						_logger.LogTrace("Failed to connect to {url} - {error}", url, e);
					}
				}
			});

			StartNetworkLoop();
		}

		public override void Disconnect()
		{
			if (_kcpConnection != null)
			{
				_kcpConnection.Disconnect();
				_kcpConnection = null;
			}

			_connectionEstablished = false;
			_connectionVerified = false;
		}

		public override void SendMessage(IDictionary<string, object> message)
		{
			DoSendRequest(message);
		}

		public override void SendBatch(IList<IDictionary<string, object>> messages)
		{
			if (messages.Count == 1)
			{
				DoSendRequest(messages[0]);
				return;
			}

			DoSendBatch(messages);
		}

		private void OnClientConnected()
		{
			_connectionEstablished = true;

			_logger.LogTrace("OnUdpClientConnected");

			ConnectionOpenedHandler?.Invoke(this);
		}

		private void OnClientDisconnected()
		{
			_logger.LogTrace("OnUdpClientDisconnected");

			StopNetworkLoop();
			_kcpConnection = null;

			if (!_connectionEstablished) // failed to establish connection
			{
				if (_config.NextUdpUrl())
				{
					_logger.LogTrace("Next udp url");
					//Connect();
					//return;
				}
			}

			ConnectionClosedHandler?.Invoke(this);
		}

		private void OnClientDataReceived(ArraySegment<byte> buffer, KcpChannel channel, bool compressed)
		{
			_logger.LogTrace("OnUdpClientDataReceived");

			_connectionVerified = true;

			var opcode = (KcpOpCode)buffer.Array[buffer.Offset];

			//if (opcode == KcpOpCodes.Heartbeat)
			//{
			//	return;
			//}

			if (opcode == KcpOpCode.SnipeResponse || opcode == KcpOpCode.SnipeResponseCompressed)
			{
				ProcessMessage(buffer, compressed || (opcode == KcpOpCode.SnipeResponseCompressed));
			}
		}

		private async void ProcessMessage(ArraySegment<byte> buffer, bool compressed)
		{
			// local copy for thread safety
			byte[] data = ArrayPool<byte>.Shared.Rent(buffer.Count);
			Array.ConstrainedCopy(buffer.Array, buffer.Offset, data, 0, buffer.Count);
			buffer = new ArraySegment<byte>(data, 0, buffer.Count);

			IDictionary<string, object> message = null;

			bool semaphoreOccupied = false;

			try
			{
				await _messageProcessingSemaphore.WaitAsync();
				semaphoreOccupied = true;

				message = await Task.Run(() => ReadMessage(buffer, compressed));
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(data);

				if (semaphoreOccupied && !_disposed)
				{
					_messageProcessingSemaphore.Release();
				}
			}

			if (message != null)
			{
				MessageReceivedHandler?.Invoke(message);
			}
		}

		private IDictionary<string, object> ReadMessage(ArraySegment<byte> buffer, bool compressed)
		{
			int len = BitConverter.ToInt32(buffer.Array, buffer.Offset + 1);

			if (len > buffer.Count - 5)
			{
				_logger.LogError($"ProcessMessage - Message length (${len} bytes) is greater than the buffer size (${buffer.Count} bytes)");
				return null;
			}

			var rawData = new ArraySegment<byte>(buffer.Array, buffer.Offset + 5, len);

			if (compressed)
			{
				rawData = _messageCompressor.Decompress(rawData);
			}

			return MessagePackDeserializer.Parse(rawData) as IDictionary<string, object>;
		}

		private async void DoSendRequest(IDictionary<string, object> message)
		{
			bool semaphoreOccupied = false;

			try
			{
				await _messageSerializationSemaphore.WaitAsync();
				semaphoreOccupied = true;

				ArraySegment<byte> msgData = await Task.Run(() => SerializeMessage(message));
				_kcpConnection?.SendData(msgData, KcpChannel.Reliable);
			}
			finally
			{
				if (semaphoreOccupied && !_disposed)
				{
					_messageSerializationSemaphore.Release();
				}
			}
		}

		private async void DoSendBatch(IList<IDictionary<string, object>> messages)
		{
			var data = new List<ArraySegment<byte>>(messages.Count);

			bool semaphoreOccupied = false;

			try
			{
				await _messageSerializationSemaphore.WaitAsync();
				semaphoreOccupied = true;

				foreach (var message in messages)
				{
					ArraySegment<byte> msgData = await Task.Run(() => SerializeMessage(message, false));

					byte[] temp = ArrayPool<byte>.Shared.Rent(msgData.Count);
					Array.ConstrainedCopy(msgData.Array, msgData.Offset, temp, 0, msgData.Count);
					data.Add(new ArraySegment<byte>(temp, 0, msgData.Count));
				}
			}
			finally
			{
				if (semaphoreOccupied && !_disposed)
				{
					_messageSerializationSemaphore.Release();
				}
			}

			_kcpConnection?.SendBatchReliable(data);

			foreach (var item in data)
			{
				ArrayPool<byte>.Shared.Return(item.Array);
			}
		}

		private ArraySegment<byte> SerializeMessage(IDictionary<string, object> message, bool writeLength = true)
		{
			int offset = 1; // opcode (1 byte)
			if (writeLength)
			{
				offset += 4; // + length (4 bytes)
			}

			Span<byte> msgData = _messageSerializer.Serialize(offset, message);

			if (_config.CompressionEnabled && msgData.Length >= _config.MinMessageBytesToCompress) // compression needed
			{
				_logger.LogTrace("compress message");
				// _logger.LogTrace("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

				Span<byte> msgContent = msgData.Slice(offset);
				byte[] compressed = _messageCompressor.Compress(msgContent);

				msgData[0] = (byte)KcpOpCode.SnipeRequestCompressed;

				if (writeLength)
				{
					BytesUtil.WriteInt(msgData, 1, compressed.Length + 4); // msg_data = opcode + length (4 bytes) + msg
				}

				byte[] buffer = _messageSerializer.GetBuffer();
				Array.ConstrainedCopy(compressed, 0, buffer, offset, compressed.Length);

				msgData = buffer.AsSpan(0, compressed.Length + offset);

				// _logger.LogTrace("Compressed:   " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
			}
			else // compression not needed
			{
				msgData[0] = (byte)KcpOpCode.SnipeRequest;
				if (writeLength)
				{
					BytesUtil.WriteInt(msgData, 1, msgData.Length - 1); // msg_data.Count = opcode (1 byte) + length (4 bytes) + msg.Length
				}
			}

			return _messageSerializer.GetBufferSegment(msgData.Length);
		}

		private void StartNetworkLoop()
		{
			_logger.LogTrace("StartNetworkLoop");

			CancellationTokenHelper.CancelAndDispose(ref _networkLoopCancellation);

			_networkLoopCancellation = new CancellationTokenSource();
			Task.Run(() => NetworkLoop(_networkLoopCancellation.Token));
			//Task.Run(() => UdpConnectionTimeout(_networkLoopCancellation.Token));
		}

		private void StopNetworkLoop()
		{
			_logger.LogTrace("StopNetworkLoop");

			CancellationTokenHelper.CancelAndDispose(ref _networkLoopCancellation);
		}

		private async void NetworkLoop(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested)
			{
				try
				{
					_kcpConnection?.Tick();
					//_analytics.PingTime = _kcpConnection?.connection?.PingTime ?? 0;
				}
				catch (Exception e)
				{
					_logger.LogTrace($"NetworkLoop - Exception: {e}");
					_analytics.TrackError("NetworkLoop error", e);
					OnClientDisconnected();
					return;
				}

				try
				{
					await Task.Delay(30, cancellation);
				}
				catch (TaskCanceledException)
				{
					// This is OK. Just terminating the task
					return;
				}
			}
		}

		//private async void UdpConnectionTimeout(CancellationToken cancellation)
		//{
		//	_logger.LogTrace("UdpConnectionTimeoutTask - start");

		//	try
		//	{
		//		await Task.Delay(2000, cancellation);
		//	}
		//	catch (TaskCanceledException)
		//	{
		//		// This is OK. Just terminating the task
		//		return;
		//	}

		//	if (cancellation == null || cancellation.IsCancellationRequested)
		//		return;
		//	if (cancellation != _networkLoopCancellation?.Token)
		//		return;

		//	if (!Connected)
		//	{
		//		_logger.LogTrace("UdpConnectionTimeoutTask - Calling Disconnect");
		//		OnClientDisconnected();
		//	}

		//	_logger.LogTrace("UdpConnectionTimeoutTask - finish");
		//}
	}
}
