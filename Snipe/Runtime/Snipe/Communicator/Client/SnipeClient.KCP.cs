using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MiniIT.MessagePack;

using kcp2k;

namespace MiniIT.Snipe
{
	public partial class SnipeClient
	{
		
		public event Action UdpConnectionFailed;
		
		public int UdpClientId { get; private set; }
		
		private KcpClient mUdpClient;
		private bool mUdpClientConnected;
		
		private const byte OPCODE_AUTHENTICATION_REQUEST = 1;
		private const byte OPCODE_AUTHENTICATION_RESPONSE = 2;
		private const byte OPCODE_AUTHENTICATED = 3;
		private const byte OPCODE_SNIPE_REQUEST = 4;
		private const byte OPCODE_SNIPE_RESPONSE = 5;
		
		public bool UdpClientConnected => mUdpClient != null && mUdpClient.connected;
		
		public double UdpConnectionTime { get; private set; }
		public double UdpDnsResolveTime => mUdpClient?.connection?.DnsResolveTime ?? default;
		public double UdpSocketConnectTime => mUdpClient?.connection?.SocketConnectTime ?? default;
		public double UdpSendHandshakeTime => mUdpClient?.connection?.SendHandshakeTime ?? default;
		
		private void ConnectUdpClient()
		{	
			if (mUdpClient != null) // already connected or trying to connect
				return;
				
			kcp2k.Log.Info = DebugLogger.Log;
			kcp2k.Log.Warning = DebugLogger.LogWarning;
			kcp2k.Log.Error = DebugLogger.LogError;
			
			mUdpClient = new KcpClient(
				OnUdpClientConnected,
				OnUdpClientDataReceived,
				OnUdpClientDisconnected);
				
			UdpClientId = 0;
			mConnectionStopwatch = Stopwatch.StartNew();
			
			mUdpClient.Connect(
				SnipeConfig.ServerUdpAddress,
				SnipeConfig.ServerUdpPort,
				true,  // NoDelay is recommended to reduce latency
				10,    // KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities
				2,     // KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode
				false, // KCP congestion window. Enabled in normal mode, disabled in turbo mode. Disable this for high scale games if connections get choked regularly
				4096,  // SendWindowSize    - KCP window size can be modified to support higher loads
				4096,  // ReceiveWindowSize - KCP window size can be modified to support higher loads. This also increases max message size
				3000,  // KCP timeout in milliseconds. Note that KCP sends a ping automatically
				Kcp.DEADLINK * 2); // KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting. default prematurely disconnects a lot of people (#3022). use 2x
			
			StartUdpNetworkLoop();
		}
		
		private void OnUdpClientConnected() 
		{
			if (mConnectionStopwatch != null)
			{
				mConnectionStopwatch.Stop();
				UdpConnectionTime = mConnectionStopwatch.Elapsed.TotalMilliseconds;
				mConnectionStopwatch = null;
			}
			
			// tunnel authentication
			DebugLogger.Log("[SnipeClient] OnUdpClientConnected - Sending tunnel authentication response");
			
			mUdpClientConnected = true;
			
			string code = SnipeConfig.ServerUdpAuthKey;
			int data_length = code.Length * 2 + 5; // opcode + length (4 bytes) + array of chars (2 bytes each)
			byte[] data = mBytesPool.Rent(data_length);
			data[0] = OPCODE_AUTHENTICATION_RESPONSE;
			
			// WriteString
			WriteInt(data, 1, code.Length);
			// unsafe
			// {
				// for (int i = 0; i < code.Length; i++)
				// {
					// fixed (byte* dataPtr = &data[i + 5])
					// {
						// char* valuePtr = (char*)dataPtr;
						// *valuePtr = code[i];
					// }
				// }
			// }
			for (int i = 0; i < code.Length; i++)
			{
				byte[] char_bytes = BitConverter.GetBytes(code[i]);
				data[i*2 + 5] = char_bytes[0];
				data[i*2 + 6] = char_bytes[1];
			}
			
			mUdpClient.Send(new ArraySegment<byte>(data, 0, data_length), KcpChannel.Reliable);
			mBytesPool.Return(data);
		}

		private void OnUdpClientDisconnected()
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDisconnected");
			
			UdpConnectionFailed?.Invoke();
			
			if (mUdpClientConnected)
			{
				Disconnect(true);
			}
			else // not connected yet, try websocket
			{
				ConnectWebSocket();
			}
		}
		
		// private void OnUdpClientError(Exception err)
		// {
			// DebugLogger.Log($"[SnipeClient] OnUdpClientError: {err.Message}");
		// }
		
		private void OnUdpClientDataReceived(ArraySegment<byte> buffer, KcpChannel channel)
		{
			DebugLogger.Log($"[SnipeClient] OnUdpClientDataReceived"); // {buffer.Count} bytes"); // {BitConverter.ToString(buffer.Array, buffer.Offset, buffer.Count)}");
			
			// get opcode
			
			var buffer_array = buffer.Array;
			
			byte opcode = buffer_array[buffer.Offset];
			
			// kcp heartbeat
			if (opcode == 200)
				return;
			
			//DebugLogger.Log($"[SnipeClient] : Received opcode {opcode}");
			
			// auth request -> auth response -> authenticated
			// handled in OnUdpClientConnected
			if (opcode == OPCODE_AUTHENTICATION_REQUEST)
			{
				return;
			}
			else if (opcode == OPCODE_AUTHENTICATED)
			{
				UdpClientId = BitConverter.ToInt32(buffer_array, buffer.Offset + 1);
				
				DebugLogger.Log($"[SnipeClient] UdpClientId = {UdpClientId}");
				
				OnConnected();
			}
			else if (opcode == OPCODE_SNIPE_RESPONSE)
			{
				int len = BitConverter.ToInt32(buffer_array, buffer.Offset + 1);
				ProcessMessage(new ArraySegment<byte>(buffer_array, buffer.Offset + 5, len));
			}
		}
		
		// private void DoSendRequestUdpClient(byte[] msg)
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
		
		private void DoSendRequestUdpClient(SnipeObject message)
		{
			byte[] buffer = mBytesPool.Rent(MessagePackSerializerNonAlloc.MaxUsedBufferSize);
			
			// offset = opcode + length (4 bytes) = 5
			var msg_data = MessagePackSerializerNonAlloc.Serialize(ref buffer, 5, message);
			
			buffer[0] = OPCODE_SNIPE_REQUEST;
			WriteInt(buffer, 1, msg_data.Count - 1); // msg_data.Count = opcode + length (4 bytes) + msg
			
			// string bytes_string = "";
			// for(int i = 0; i < msg_data.Count; i++)
				// bytes_string += $"{msg_data.Array[msg_data.Offset + i]} ";
			// DebugLogger.Log($"[SnipeClient] ++++ msg_data (size = {msg_data.Count}): {bytes_string}"); //{BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count)}");
			
			mUdpClient.Send(msg_data, KcpChannel.Reliable);
			
			mBytesPool.Return(buffer);
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
		
		private CancellationTokenSource mUdpNetworkLoopCancellation;

		private void StartUdpNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StartUdpNetworkLoop");
			
			mUdpNetworkLoopCancellation?.Cancel();

			mUdpNetworkLoopCancellation = new CancellationTokenSource();
			Task.Run(() => UdpNetworkLoop(mUdpNetworkLoopCancellation.Token));
		}

		private void StopUdpNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StopUdpNetworkLoop");
			
			if (mUdpNetworkLoopCancellation != null)
			{
				mUdpNetworkLoopCancellation.Cancel();
				mUdpNetworkLoopCancellation = null;
			}
		}

		private async void UdpNetworkLoop(CancellationToken cancellation)
		{
			DebugLogger.Log("[SnipeClient] UdpNetworkLoop - start");
			
			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				mUdpClient?.TickIncoming();
				mUdpClient?.TickOutgoing();
				Analytics.PingTime = mUdpClient?.connection?.PingTime ?? 0;
				await Task.Yield();
			}
			
			DebugLogger.Log("[SnipeClient] UdpNetworkLoop - finish");
		}
		
	}
}