using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using MiniIT.MessagePack;

using kcp2k;
using System.Buffers;
using System.Reflection.Emit;

namespace MiniIT.Snipe
{
	public class KcpConnection : SnipeConnection
	{
		private const byte OPCODE_AUTHENTICATION_REQUEST = 1;
		// private const byte OPCODE_AUTHENTICATION_RESPONSE = 2;
		// private const byte OPCODE_AUTHENTICATED = 3;
		private const byte OPCODE_SNIPE_REQUEST = 4;
		private const byte OPCODE_SNIPE_RESPONSE = 5;
		private const byte OPCODE_SNIPE_REQUEST_COMPRESSED = 6;
		private const byte OPCODE_SNIPE_RESPONSE_COMPRESSED = 7;

		public bool ConnectionEstablished { get; private set; } = false;
		
		public bool Started => mUdpClient != null;
		public bool Connected => mUdpClient != null && mUdpClient.connected;
		
		public double UdpDnsResolveTime { get; private set; }
		public double UdpSocketConnectTime { get; private set; }
		public double UdpSendHandshakeTime { get; private set; }

		private KcpClient mUdpClient;

		public void Connect()
		{	
			if (mUdpClient != null) // already connected or trying to connect
				return;

			ConnectionEstablished = false;

			kcp2k.Log.Info = DebugLogger.Log;
			kcp2k.Log.Warning = DebugLogger.LogWarning;
			kcp2k.Log.Error = DebugLogger.LogError;
			
			mUdpClient = new KcpClient(
				OnClientConnected,
				OnClientDataReceived,
				OnClientDisconnected);

			var address = SnipeConfig.GetUdpAddress();

			mUdpClient.Connect(
				address.Host,
				address.Port,
				true,  // NoDelay is recommended to reduce latency
				10,    // KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities
				2,     // KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode
				false, // KCP congestion window. Enabled in normal mode, disabled in turbo mode. Disable this for high scale games if connections get choked regularly
				4096,  // SendWindowSize    - KCP window size can be modified to support higher loads
				4096,  // ReceiveWindowSize - KCP window size can be modified to support higher loads. This also increases max message size
				3000,  // KCP timeout in milliseconds. Note that KCP sends a ping automatically
				Kcp.DEADLINK * 2); // KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting. default prematurely disconnects a lot of people (#3022). use 2x
			
			StartNetworkLoop();
		}

		public void Disconnect()
		{
			if (mUdpClient != null)
			{
				mUdpClient.Disconnect();
				mUdpClient = null;
			}
			ConnectionEstablished = false;
		}

		public void SendMessage(SnipeObject message)
		{
			DoSendRequest(message);
		}

		private void OnClientConnected() 
		{
			ConnectionEstablished = true;

			RefreshConnectionStats();
			
			DebugLogger.Log("[SnipeClient] OnUdpClientConnected");

			ConnectionOpenedHandler?.Invoke();
		}

		private void OnClientDisconnected()
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDisconnected");
			
			RefreshConnectionStats();

			StopNetworkLoop();
			mUdpClient = null;

			if (!ConnectionEstablished) // failed to establish connection
			{
				if (SnipeConfig.NextUdpUrl())
				{
					DebugLogger.Log("[SnipeClient] Next udp url");
					Connect();
					return;
				}
			}

			ConnectionClosedHandler?.Invoke();
		}
		
		// private void OnClientError(Exception err)
		// {
			// DebugLogger.Log($"[SnipeClient] OnUdpClientError: {err.Message}");
		// }
		
		private void RefreshConnectionStats()
		{
			UdpDnsResolveTime = mUdpClient?.connection?.DnsResolveTime ?? 0;
			UdpSocketConnectTime = mUdpClient?.connection?.SocketConnectTime ?? 0;
			UdpSendHandshakeTime = mUdpClient?.connection?.SendHandshakeTime ?? 0;
		}
		
		private void OnClientDataReceived(ArraySegment<byte> buffer, KcpChannel channel)
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

			if (opcode == OPCODE_SNIPE_RESPONSE || opcode == OPCODE_SNIPE_RESPONSE_COMPRESSED)
			{
				ProcessMessage(buffer);
			}
		}

		private async void ProcessMessage(ArraySegment<byte> buffer)
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

				if (opcode == OPCODE_SNIPE_RESPONSE_COMPRESSED)
				{
					var decompressed_data = mMessageCompressor.Decompress(raw_data);

					return MessagePackDeserializer.Parse(decompressed_data) as SnipeObject;
				}

				return MessagePackDeserializer.Parse(raw_data) as SnipeObject;
			});

			MessageReceivedHandler?.Invoke(message);

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
			// mUdpClient.Send(new ArraySegment<byte>(data, 0, data_length), KcpChannel.Reliable);
			// mBytesPool.Return(data);
		// }
		
		private async void DoSendRequest(SnipeObject message)
		{
			string message_type = message.SafeGetString("t");
			
			byte[] buffer = mMessageBufferProvider.GetBuffer(message_type);

			// offset = opcode (1 byte) + length (4 bytes) = 5
			ArraySegment<byte> msg_data = await Task.Run(() => MessagePackSerializerNonAlloc.Serialize(ref buffer, 5, message));

			if (SnipeConfig.CompressionEnabled && msg_data.Count >= SnipeConfig.MinMessageSizeToCompress) // compression needed
			{
				await Task.Run(() =>
				{
					DebugLogger.Log("[SnipeClient] compress message");
					DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> msg_content = new ArraySegment<byte>(buffer, 5, msg_data.Count - 5);
					ArraySegment<byte> compressed = mMessageCompressor.Compress(msg_content);

					mMessageBufferProvider.ReturnBuffer(message_type, buffer);

					buffer = ArrayPool<byte>.Shared.Rent(compressed.Count + 5);
					buffer[0] = OPCODE_SNIPE_REQUEST_COMPRESSED;
					WriteInt(buffer, 1, compressed.Count + 4); // msg_data = opcode + length (4 bytes) + msg
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, buffer, 5, compressed.Count);

					msg_data = new ArraySegment<byte>(buffer, 0, compressed.Count + 5);

					DebugLogger.Log("Compressed:   " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
				});

				mUdpClient.Send(msg_data, KcpChannel.Reliable);
				ArrayPool<byte>.Shared.Return(buffer);
			}
			else // compression not needed
			{
				buffer[0] = OPCODE_SNIPE_REQUEST;
				WriteInt(buffer, 1, msg_data.Count - 1); // msg_data.Count = opcode (1 byte) + length (4 bytes) + msg.Lenght

				mUdpClient.Send(msg_data, KcpChannel.Reliable);

				mMessageBufferProvider.ReturnBuffer(message_type, buffer);
			}
			

			//----
			//DebugLogger.Log(BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

			//ArraySegment<byte> msg_content = new ArraySegment<byte>(buffer, 5, msg_data.Count - 5);
			//ArraySegment<byte> compressed = SnipeMessageCompressor.Compress(msg_content);

			//TryReturnMessageBuffer(buffer, message_type, buffer_size);

			//---

			//if (compressed.Count < msg_data.Count - 5)
			//{
			//buffer = mBytesPool.Rent(compressed.Count + 5);
			//buffer[0] = OPCODE_SNIPE_REQUEST_COMPRESSED;
			//WriteInt(buffer, 1, compressed.Count + 4); // msg_data = opcode + length (4 bytes) + msg
			//Array.ConstrainedCopy(compressed.Array, compressed.Offset, buffer, 5, compressed.Count);

			//msg_data = new ArraySegment<byte>(buffer, 0, compressed.Count + 5);
			//DebugLogger.Log(BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
			//DebugLogger.Log(BitConverter.ToString(compressed.Array, compressed.Offset, compressed.Count));

			// test decompression
			//var decompression_buffer = mBytesPool.Rent(buffer_size);
			//var decompressed_data = SnipeMessageCompressor.Decompress(ref decompression_buffer, compressed);
			//DebugLogger.Log(BitConverter.ToString(decompressed_data.Array, decompressed_data.Offset, decompressed_data.Count));
			//var decompressed_message = MessagePackDeserializer.Parse(decompressed_data) as SnipeObject;
			//DebugLogger.Log($"decompressed_message: {decompressed_message.ToJSONString()}");
			//try
			//{
			//	mBytesPool.Return(decompression_buffer);
			//}
			//catch (ArgumentException)
			//{
			//	// ignore
			//}
			//}
			//----
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void WriteInt(byte[] data, int position, int value)
        {
            // unsafe
            // {
                // fixed (byte* dataPtr = &data[position])
                // {
                    // int* valuePtr = (int*)dataPtr;
                    // *valuePtr = value;
                // }
            // }
			data[position] = (byte) value;
			data[position + 1] = (byte) (value >> 8);
			data[position + 2] = (byte) (value >> 0x10);
			data[position + 3] = (byte) (value >> 0x18);
        }
		
		private CancellationTokenSource mNetworkLoopCancellation;

		private void StartNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StartNetworkLoop");
			
			mNetworkLoopCancellation?.Cancel();

			mNetworkLoopCancellation = new CancellationTokenSource();
			Task.Run(() => NetworkLoop(mNetworkLoopCancellation.Token));
			Task.Run(() => UdpConnectionTimeout(mNetworkLoopCancellation.Token));
		}

		public void StopNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StopNetworkLoop");
			
			if (mNetworkLoopCancellation != null)
			{
				mNetworkLoopCancellation.Cancel();
				mNetworkLoopCancellation = null;
			}
		}

		private async void NetworkLoop(CancellationToken cancellation)
		{
			DebugLogger.Log("[SnipeClient] NetworkLoop - start");
			
			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				try
				{
					mUdpClient?.TickIncoming();
					mUdpClient?.TickOutgoing();
					Analytics.PingTime = mUdpClient?.connection?.PingTime ?? 0;
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
			
			DebugLogger.Log("[SnipeClient] NetworkLoop - finish");
		}
		
		private async void UdpConnectionTimeout(CancellationToken cancellation)
		{
			DebugLogger.Log("[SnipeClient] UdpConnectionTimeout - start");
			
			try
			{
				await Task.Delay(2000, cancellation);
			}
			catch (TaskCanceledException)
			{
				// This is OK. Just terminating the task
				return;
			}
			
			if (cancellation == null || cancellation.IsCancellationRequested)
				return;
			if (cancellation != mNetworkLoopCancellation?.Token)
				return;
			
			if (!Connected)
			{
				DebugLogger.Log("[SnipeClient] UdpConnectionTimeout - Calling Disconnect");
				OnClientDisconnected();
			}
			
			DebugLogger.Log("[SnipeClient] UdpConnectionTimeout - finish");
		}
	}
}