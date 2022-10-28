using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniIT.MessagePack;

using kcp2k;
using System.Buffers;

namespace MiniIT.Snipe
{
	public class KcpTransport : Transport
	{
		internal const byte OPCODE_AUTHENTICATION_REQUEST = 1;
		// internal const byte OPCODE_AUTHENTICATION_RESPONSE = 2;
		// internal const byte OPCODE_AUTHENTICATED = 3;
		internal const byte OPCODE_SNIPE_REQUEST = 4;
		internal const byte OPCODE_SNIPE_RESPONSE = 5;
		internal const byte OPCODE_SNIPE_REQUEST_COMPRESSED = 6;
		internal const byte OPCODE_SNIPE_RESPONSE_COMPRESSED = 7;

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
			using (var serializer = new KcpMessageSerializer(message, mMessageCompressor, mMessageBufferProvider))
			{
				ArraySegment<byte> msg_data = await serializer.Run();
				mUdpClient.Send(msg_data, KcpChannel.Reliable);
			}
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