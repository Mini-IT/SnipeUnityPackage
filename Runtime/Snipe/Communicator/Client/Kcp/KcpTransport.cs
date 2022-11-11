using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniIT.MessagePack;

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
		public bool Connected => mUdpClient != null && mUdpClient.Connected;
		
		public double UdpDnsResolveTime { get; private set; }
		public double UdpSocketConnectTime { get; private set; }
		public double UdpSendHandshakeTime { get; private set; }

		private KcpConnection mUdpClient;
		private CancellationTokenSource mNetworkLoopCancellation;

		private readonly object mLock = new object();

		public void Connect()
		{
			Task.Run(() =>
			{
				lock (mLock)
				{
					ConnectTask();
				}
			});
		}

		private void ConnectTask()
		{
			if (mUdpClient != null) // already connected or trying to connect
				return;

			ConnectionEstablished = false;

			mUdpClient = new KcpConnection();
			mUdpClient.OnAuthenticated = OnClientConnected;
			mUdpClient.OnData = OnClientDataReceived;
			mUdpClient.OnDisconnected = OnClientDisconnected;

			var address = SnipeConfig.GetUdpAddress();

			mUdpClient.Connect(address.Host, address.Port, 3000);
			
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

			mMainThreadActions.Enqueue(() =>
			{
				ConnectionOpenedHandler?.Invoke();
			});
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

			mMainThreadActions.Enqueue(() =>
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
			//UdpDnsResolveTime = mUdpClient?.connection?.DnsResolveTime ?? 0;
			//UdpSocketConnectTime = mUdpClient?.connection?.SocketConnectTime ?? 0;
			//UdpSendHandshakeTime = mUdpClient?.connection?.SendHandshakeTime ?? 0;
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

			if (opcode == OPCODE_SNIPE_RESPONSE || opcode == OPCODE_SNIPE_RESPONSE_COMPRESSED)
			{
				ProcessMessage(buffer, compressed || (opcode == OPCODE_SNIPE_RESPONSE_COMPRESSED));
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
					var decompressed_data = mMessageCompressor.Decompress(raw_data);

					return MessagePackDeserializer.Parse(decompressed_data) as SnipeObject;
				}

				return MessagePackDeserializer.Parse(raw_data) as SnipeObject;
			});

			mMainThreadActions.Enqueue(() =>
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
			// mUdpClient.Send(new ArraySegment<byte>(data, 0, data_length), KcpChannel.Reliable);
			// mBytesPool.Return(data);
		// }
		
		private async void DoSendRequest(SnipeObject message)
		{
			using (var serializer = new KcpMessageSerializer(message, mMessageCompressor, mMessageBufferProvider))
			{
				ArraySegment<byte> msg_data = await serializer.Run();
				lock (mLock)
				{
					mUdpClient.SendData(msg_data, KcpChannel.Reliable);
				}
			}
		}
		
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
			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				try
				{
					mUdpClient?.Tick();
					//Analytics.PingTime = mUdpClient?.connection?.PingTime ?? 0;
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
		
		private async void UdpConnectionTimeout(CancellationToken cancellation)
		{
			DebugLogger.Log("[SnipeClient] UdpConnectionTimeoutTask - start");
			
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
				DebugLogger.Log("[SnipeClient] UdpConnectionTimeoutTask - Calling Disconnect");
				OnClientDisconnected();
			}
			
			DebugLogger.Log("[SnipeClient] UdpConnectionTimeoutTask - finish");
		}
	}
}