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
		
		public bool Started => _kcpConnection != null;
		public bool Connected => _kcpConnection != null && _kcpConnection.Connected;
		
		public double UdpDnsResolveTime { get; private set; }
		public double UdpSocketConnectTime { get; private set; }
		public double UdpSendHandshakeTime { get; private set; }

		private KcpConnection _kcpConnection;
		private CancellationTokenSource _networkLoopCancellation;

		private readonly object _lock = new object();

		public async void Connect()
		{
			if (_kcpConnection != null) // already connected or trying to connect
				return;

			ConnectionEstablished = false;

			_kcpConnection = new KcpConnection();
			_kcpConnection.OnAuthenticated = OnClientConnected;
			_kcpConnection.OnData = OnClientDataReceived;
			_kcpConnection.OnDisconnected = OnClientDisconnected;

			await Task.Run(() =>
			{
				lock (_lock)
				{
					var address = SnipeConfig.GetUdpAddress();
					_kcpConnection.Connect(address.Host, address.Port, 3000, 5000);
				}
			});

			StartNetworkLoop();
		}

		public void Disconnect()
		{
			if (_kcpConnection != null)
			{
				_kcpConnection.Disconnect();
				_kcpConnection = null;
			}
			ConnectionEstablished = false;
		}

		public void SendMessage(SnipeObject message)
		{
			DoSendRequest(message);
		}

		public void SendBatch(List<SnipeObject> messages)
		{
			DoSendBatch(messages);
		}

		private void OnClientConnected() 
		{
			ConnectionEstablished = true;

			RefreshConnectionStats();
			
			DebugLogger.Log("[SnipeClient] OnUdpClientConnected");

			_mainThreadActions.Enqueue(() =>
			{
				ConnectionOpenedHandler?.Invoke();
			});
		}

		private void OnClientDisconnected()
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDisconnected");
			
			RefreshConnectionStats();

			StopNetworkLoop();
			_kcpConnection = null;

			if (!ConnectionEstablished) // failed to establish connection
			{
				if (SnipeConfig.NextUdpUrl())
				{
					DebugLogger.Log("[SnipeClient] Next udp url");
					Connect();
					return;
				}
			}

			_mainThreadActions.Enqueue(() =>
			{
				ConnectionClosedHandler?.Invoke();
			});
		}
		
		// private void OnClientError(Exception err)
		// {
			// DebugLogger.Log($"[SnipeClient] OnUdpClientError: {err.Message}");
		// }
		
		private void RefreshConnectionStats()
		{
			//UdpDnsResolveTime = _kcpConnection?.connection?.DnsResolveTime ?? 0;
			//UdpSocketConnectTime = _kcpConnection?.connection?.SocketConnectTime ?? 0;
			//UdpSendHandshakeTime = _kcpConnection?.connection?.SendHandshakeTime ?? 0;
		}
		
		private void OnClientDataReceived(ArraySegment<byte> buffer, KcpChannel channel, bool compressed)
		{
			DebugLogger.Log($"[SnipeClient] OnUdpClientDataReceived");
			
			// get opcode
			
			byte opcode = buffer.Array[buffer.Offset];
			
			// kcp heartbeat
			if (opcode == 200)
				return;
			
			//DebugLogger.Log($"[SnipeClient] : Received opcode {opcode}");
			
			// auth request -> auth response -> authenticated
			// handled in OnUdpClientConnected
			//if (opcode == OPCODE_AUTHENTICATION_REQUEST)
			//{
			//	return;
			//}

			if (opcode == (byte)KcpOpCodes.SnipeResponse || opcode == (byte)KcpOpCodes.SnipeResponseCompressed)
			{
				ProcessMessage(buffer, compressed || (opcode == (byte)KcpOpCodes.SnipeResponseCompressed));
			}
		}

		private async void ProcessMessage(ArraySegment<byte> buffer, bool compressed)
		{
			// local copy for thread safety
			byte[] data = ArrayPool<byte>.Shared.Rent(buffer.Count);
			Array.ConstrainedCopy(buffer.Array, buffer.Offset, data, 0, buffer.Count);
			buffer = new ArraySegment<byte>(data, 0, buffer.Count);

			SnipeObject message = await Task.Run(() =>
			{
				byte opcode = buffer.Array[buffer.Offset];

				int len = BitConverter.ToInt32(buffer.Array, buffer.Offset + 1);
				var raw_data = new ArraySegment<byte>(buffer.Array, buffer.Offset + 5, len);

				if (compressed)
				{
					var decompressed_data = _messageCompressor.Decompress(raw_data);

					return MessagePackDeserializer.Parse(decompressed_data) as SnipeObject;
				}

				return MessagePackDeserializer.Parse(raw_data) as SnipeObject;
			});

			_mainThreadActions.Enqueue(() =>
			{
				MessageReceivedHandler?.Invoke(message);
			});

			try
			{
				ArrayPool<byte>.Shared.Return(data);
			}
			catch (Exception)
			{
				//ignore
			}
		}
		
		// private void DoSendRequest(byte[] msg)
		// {
			// // opcode + length (4 bytes) + msg
			// int data_length = msg.Length + 5;
			// byte[] data = mBytesPool.Rent(data_length);
			// data[0] = OPCODE_SNIPE_REQUEST;
			// WriteInt(data, 1, msg.Length);
			// Array.ConstrainedCopy(msg, 0, data, 5, msg.Length);
			// _kcpConnection.Send(new ArraySegment<byte>(data, 0, data_length), KcpChannel.Reliable);
			// mBytesPool.Return(data);
		// }
		
		private async void DoSendRequest(SnipeObject message)
		{
			try
			{
				await _messageSerializationSemaphore.WaitAsync();

				ArraySegment<byte> msg_data = await SerializeMessage(message);
				_kcpConnection.SendData(msg_data, KcpChannel.Reliable);
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

			_kcpConnection.SendBatchReliable(data);

			foreach (var item in data)
			{
				ArrayPool<byte>.Shared.Return(item.Array);
			}
		}

		public async Task<ArraySegment<byte>> SerializeMessage(SnipeObject message, bool writeLength = true)
		{
			int offset = 1; // opcode (1 byte)
			if (writeLength)
			{
				offset += 4; // + length (4 bytes)
			}

			ArraySegment<byte> msg_data = await Task.Run(() => MessagePackSerializerNonAlloc.Serialize(ref _messageSerializationBuffer, offset, message));

			if (SnipeConfig.CompressionEnabled && msg_data.Count >= SnipeConfig.MinMessageSizeToCompress) // compression needed
			{
				await Task.Run(() =>
				{
					DebugLogger.Log("[SnipeClient] compress message");
					// DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> msg_content = new ArraySegment<byte>(_messageSerializationBuffer, offset, msg_data.Count - offset);
					ArraySegment<byte> compressed = _messageCompressor.Compress(msg_content);

					_messageSerializationBuffer[0] = (byte)KcpOpCodes.SnipeRequestCompressed;

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
				_messageSerializationBuffer[0] = (byte)KcpOpCodes.SnipeRequest;
				if (writeLength)
				{
					BytesUtil.WriteInt(_messageSerializationBuffer, 1, msg_data.Count - 1); // msg_data.Count = opcode (1 byte) + length (4 bytes) + msg.Lenght
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
					//Analytics.PingTime = _kcpConnection?.connection?.PingTime ?? 0;
				}
				catch (Exception e)
				{
					DebugLogger.Log($"[SnipeClient] NetworkLoop - Exception: {e}");
					Analytics.TrackError("NetworkLoop error", e);
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