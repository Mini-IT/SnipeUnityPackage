using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public class KcpTransport : Transport
	{
		public override bool Started => _kcpConnection != null;
		public override bool Connected => _kcpConnection != null && _kcpConnection.Connected;
		public override bool ConnectionEstablished => _connectionEstablished;

		private KcpConnection _kcpConnection;
		private CancellationTokenSource _networkLoopCancellation;

		private bool _connectionEstablished = false;
		private readonly object _lock = new object();

		internal KcpTransport(SnipeConfig config, SnipeAnalyticsTracker analytics)
			: base(config, analytics)
		{
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
					_analytics.ConnectionUrl = $"{address.Host}:{address.Port}";
					_kcpConnection.Connect(address.Host, address.Port, 3000, 5000);
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
		}

		public override void SendMessage(SnipeObject message)
		{
			DoSendRequest(message);
		}

		public override void SendBatch(List<SnipeObject> messages)
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

			SnipeObject message = null;

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

				if (semaphoreOccupied)
				{
					_messageProcessingSemaphore.Release();
				}
			}
			
			if (message != null)
			{
				MessageReceivedHandler?.Invoke(message);
			}
		}
		
		private SnipeObject ReadMessage(ArraySegment<byte> buffer, bool compressed)
		{
			int len = BitConverter.ToInt32(buffer.Array, buffer.Offset + 1);
			
			if (len > buffer.Count - 5)
			{
				_logger.LogError($"ProcessMessage - Message length (${len} bytes) is greater than the buffer size (${buffer.Count} bytes)");
				return null;
			}
			
			var raw_data = new ArraySegment<byte>(buffer.Array, buffer.Offset + 5, len);

			if (compressed)
			{
				raw_data = _messageCompressor.Decompress(raw_data);
			}

			return MessagePackDeserializer.Parse(raw_data) as SnipeObject;
		}
		
		private async void DoSendRequest(SnipeObject message)
		{
			bool semaphoreOccupied = false;

			try
			{
				await _messageSerializationSemaphore.WaitAsync();
				semaphoreOccupied = true;

				ArraySegment<byte> msg_data = await SerializeMessage(message);
				_kcpConnection?.SendData(msg_data, KcpChannel.Reliable);
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_messageSerializationSemaphore.Release();
				}
			}
		}

		private async void DoSendBatch(List<SnipeObject> messages)
		{
			var data = new List<ArraySegment<byte>>(messages.Count);

			bool semaphoreOccupied = false;

			try
			{
				await _messageSerializationSemaphore.WaitAsync();
				semaphoreOccupied = true;

				foreach (var message in messages)
				{
					ArraySegment<byte> msg_data = await SerializeMessage(message, false);

					byte[] temp = ArrayPool<byte>.Shared.Rent(msg_data.Count);
					Array.ConstrainedCopy(msg_data.Array, msg_data.Offset, temp, 0, msg_data.Count);
					data.Add(new ArraySegment<byte>(temp, 0, msg_data.Count));
				}
			}
			finally
			{
				if (semaphoreOccupied)
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

		private async Task<ArraySegment<byte>> SerializeMessage(SnipeObject message, bool writeLength = true)
		{
			int offset = 1; // opcode (1 byte)
			if (writeLength)
			{
				offset += 4; // + length (4 bytes)
			}

			ArraySegment<byte> msg_data = await Task.Run(() => _messageSerializer.Serialize(ref _messageSerializationBuffer, offset, message));

			if (_config.CompressionEnabled && msg_data.Count >= _config.MinMessageBytesToCompress) // compression needed
			{
				await Task.Run(() =>
				{
					_logger.LogTrace("compress message");
					// _logger.LogTrace("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> msg_content = new ArraySegment<byte>(_messageSerializationBuffer, offset, msg_data.Count - offset);
					ArraySegment<byte> compressed = _messageCompressor.Compress(msg_content);

					_messageSerializationBuffer[0] = (byte)KcpOpCode.SnipeRequestCompressed;

					if (writeLength)
					{
						BytesUtil.WriteInt(_messageSerializationBuffer, 1, compressed.Count + 4); // msg_data = opcode + length (4 bytes) + msg
					}
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, _messageSerializationBuffer, offset, compressed.Count);

					msg_data = new ArraySegment<byte>(_messageSerializationBuffer, 0, compressed.Count + offset);

					// _logger.LogTrace("Compressed:   " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
				});
			}
			else // compression not needed
			{
				_messageSerializationBuffer[0] = (byte)KcpOpCode.SnipeRequest;
				if (writeLength)
				{
					BytesUtil.WriteInt(_messageSerializationBuffer, 1, msg_data.Count - 1); // msg_data.Count = opcode (1 byte) + length (4 bytes) + msg.Length
				}
			}

			return msg_data;
		}

		private void StartNetworkLoop()
		{
			_logger.LogTrace("StartNetworkLoop");
			
			_networkLoopCancellation?.Cancel();

			_networkLoopCancellation = new CancellationTokenSource();
			Task.Run(() => NetworkLoop(_networkLoopCancellation.Token));
			//Task.Run(() => UdpConnectionTimeout(_networkLoopCancellation.Token));
		}

		public void StopNetworkLoop()
		{
			_logger.LogTrace("StopNetworkLoop");
			
			if (_networkLoopCancellation != null)
			{
				_networkLoopCancellation.Cancel();
				_networkLoopCancellation = null;
			}
		}

		private async void NetworkLoop(CancellationToken cancellation)
		{
			while (cancellation != null && !cancellation.IsCancellationRequested)
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
					await Task.Delay(100, cancellation);
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
