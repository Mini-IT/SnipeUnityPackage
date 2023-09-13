using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public class KcpTransport : Transport
	{
		public bool ConnectionEstablished { get; private set; } = false;
		
		public override bool Started => _kcpConnection != null;
		public override bool Connected => _kcpConnection != null && _kcpConnection.Connected;
		
		private KcpConnection _kcpConnection;
		private CancellationTokenSource _networkLoopCancellation;

		private readonly object _lock = new object();
		private readonly SnipeConfig _config;
		private readonly Analytics _analytics;

		internal KcpTransport(SnipeConfig config, Analytics analytics)
		{
			_config = config;
			_analytics = analytics;
		}

		public override void Connect()
		{
			lock (_lock)
			{
				if (_kcpConnection != null) // already connected or trying to connect
					return;

				ConnectionEstablished = false;

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
			ConnectionEstablished = false;
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
			ConnectionEstablished = true;

			DebugLogger.Log("[SnipeClient] OnUdpClientConnected");

			ConnectionOpenedHandler?.Invoke();
		}

		private void OnClientDisconnected()
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDisconnected");
			
			StopNetworkLoop();
			_kcpConnection = null;

			if (!ConnectionEstablished) // failed to establish connection
			{
				if (_config.NextUdpUrl())
				{
					DebugLogger.Log("[SnipeClient] Next udp url");
					//Connect();
					//return;
				}
			}

			ConnectionClosedHandler?.Invoke();
		}
		
		private void OnClientDataReceived(ArraySegment<byte> buffer, KcpChannel channel, bool compressed)
		{
			DebugLogger.Log($"[SnipeClient] OnUdpClientDataReceived");
			
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

			try
			{
				await _messageProcessingSemaphore.WaitAsync();

				message = await Task.Run(() => ReadMessage(buffer, compressed));
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(data);
				
				_messageProcessingSemaphore.Release();
			}
			
			if (message != null)
			{
				MessageReceivedHandler?.Invoke(message);
			}
		}
		
		private SnipeObject ReadMessage(ArraySegment<byte> buffer, bool compressed)
		{
			// byte opcode = buffer.Array[buffer.Offset];

			int len = BitConverter.ToInt32(buffer.Array, buffer.Offset + 1);
			
			if (len > buffer.Count - 5)
			{
				DebugLogger.LogError($"[KcpTransport] ProcessMessage - Message lenght (${len} bytes) is greater than the buffer size (${buffer.Count} bytes)");
				return null;
			}
			
			var raw_data = new ArraySegment<byte>(buffer.Array, buffer.Offset + 5, len);

			if (compressed)
			{
				var decompressed_data = _messageCompressor.Decompress(raw_data);

				return MessagePackDeserializer.Parse(decompressed_data) as SnipeObject;
			}

			return MessagePackDeserializer.Parse(raw_data) as SnipeObject;
		}
		
		private async void DoSendRequest(SnipeObject message)
		{
			try
			{
				await _messageSerializationSemaphore.WaitAsync();

				ArraySegment<byte> msg_data = await SerializeMessage(message);
				_kcpConnection?.SendData(msg_data, KcpChannel.Reliable);
			}
			finally
			{
				_messageSerializationSemaphore.Release();
			}
		}

		private async void DoSendBatch(List<SnipeObject> messages)
		{
			var data = new List<ArraySegment<byte>>(messages.Count);

			try
			{
				await _messageSerializationSemaphore.WaitAsync();

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
				_messageSerializationSemaphore.Release();
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
					DebugLogger.Log("[SnipeClient] compress message");
					// DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> msg_content = new ArraySegment<byte>(_messageSerializationBuffer, offset, msg_data.Count - offset);
					ArraySegment<byte> compressed = _messageCompressor.Compress(msg_content);

					_messageSerializationBuffer[0] = (byte)KcpOpCode.SnipeRequestCompressed;

					if (writeLength)
					{
						BytesUtil.WriteInt(_messageSerializationBuffer, 1, compressed.Count + 4); // msg_data = opcode + length (4 bytes) + msg
					}
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, _messageSerializationBuffer, offset, compressed.Count);

					msg_data = new ArraySegment<byte>(_messageSerializationBuffer, 0, compressed.Count + offset);

					// DebugLogger.Log("Compressed:   " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
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
			DebugLogger.Log("[SnipeClient] StartNetworkLoop");
			
			_networkLoopCancellation?.Cancel();

			_networkLoopCancellation = new CancellationTokenSource();
			Task.Run(() => NetworkLoop(_networkLoopCancellation.Token));
			//Task.Run(() => UdpConnectionTimeout(_networkLoopCancellation.Token));
		}

		public void StopNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StopNetworkLoop");
			
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
					DebugLogger.Log($"[SnipeClient] NetworkLoop - Exception: {e}");
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
		//	DebugLogger.Log("[SnipeClient] UdpConnectionTimeoutTask - start");
			
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
		//		DebugLogger.Log("[SnipeClient] UdpConnectionTimeoutTask - Calling Disconnect");
		//		OnClientDisconnected();
		//	}
			
		//	DebugLogger.Log("[SnipeClient] UdpConnectionTimeoutTask - finish");
		//}
	}
}
