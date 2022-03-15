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
		// private const byte OPCODE_AUTHENTICATION_RESPONSE = 2;
		private const byte OPCODE_AUTHENTICATED = 3;
		private const byte OPCODE_SNIPE_REQUEST = 4;
		private const byte OPCODE_SNIPE_RESPONSE = 5;
		
		public bool UdpClientConnected => mUdpClient != null && mUdpClient.connected;
		
		public double UdpConnectionTime { get; private set; }
		public double UdpDnsResolveTime { get; private set; }
		public double UdpSocketConnectTime { get; private set; }
		public double UdpSendHandshakeTime { get; private set; }
		
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
			
			RefreshConnectionStats();
			
			mUdpClientConnected = true;
			
			DebugLogger.Log("[SnipeClient] OnUdpClientConnected");
		}

		private void OnUdpClientDisconnected()
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDisconnected");
			
			RefreshConnectionStats();
			
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
		
		private void RefreshConnectionStats()
		{
			UdpDnsResolveTime = mUdpClient?.connection?.DnsResolveTime ?? 0;
			UdpSocketConnectTime = mUdpClient?.connection?.SocketConnectTime ?? 0;
			UdpSendHandshakeTime = mUdpClient?.connection?.SendHandshakeTime ?? 0;
		}
		
		private void OnUdpClientDataReceived(ArraySegment<byte> buffer, KcpChannel channel)
		{
			DebugLogger.Log($"[SnipeClient] OnUdpClientDataReceived");
			
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
			string message_type = message.SafeGetString("t");
			
			int buffer_size;
			if (!mSendMessageBufferSizes.TryGetValue(message_type, out buffer_size))
			{
				buffer_size = 1024;
			}
			
			byte[] buffer = mBytesPool.Rent(buffer_size);
			
			// offset = opcode + length (4 bytes) = 5
			var msg_data = MessagePackSerializerNonAlloc.Serialize(ref buffer, 5, message);
			
			buffer[0] = OPCODE_SNIPE_REQUEST;
			WriteInt(buffer, 1, msg_data.Count - 1); // msg_data.Count = opcode + length (4 bytes) + msg
			
			mUdpClient.Send(msg_data, KcpChannel.Reliable);
			
			// if buffer.Length > mBytesPool's max bucket size (1024*1024 = 1048576)
			// then the buffer can not be returned to the pool. It will be dropped.
			// And ArgumentException will be thown.
			try
			{
				mBytesPool.Return(buffer);
				
				if (buffer.Length > buffer_size)
				{
					mSendMessageBufferSizes[message_type] = buffer.Length;
				}
			}
			catch (ArgumentException)
			{
				// ignore
			}
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
				await Task.Delay(100);
			}
			
			DebugLogger.Log("[SnipeClient] UdpNetworkLoop - finish");
		}
		
	}
}