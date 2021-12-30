using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;

using kcp2k;

namespace MiniIT.Snipe
{
	public partial class SnipeClient
	{
		
		public event Action UdpConnectionFailed;
		
		#region UdpClient
		
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
				
			// kcp2k.Log.Info = DebugLogger.Log;
			// kcp2k.Log.Warning = DebugLogger.LogWarning;
			// kcp2k.Log.Error = DebugLogger.LogError;
			
			mUdpClient = new KcpClient(
				OnUdpClientConnected,
				(message, channel) => OnUdpClientDataReceived(message),
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
			
			var code = SnipeConfig.ServerUdpAuthKey;
			int pos = 0;
			byte[] data = new byte[code.Length * 2 + 5];
			data.WriteByte(ref pos, OPCODE_AUTHENTICATION_RESPONSE);
			data.WriteString(ref pos, code);
			mUdpClient.Send(new ArraySegment<byte>(data, 0, pos), KcpChannel.Reliable);
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
		
		private void OnUdpClientDataReceived(ArraySegment<byte> b) //, int channel)
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDataReceived");
			
			var data = b.Array;
			int pos = b.Offset;
				
			// get opcode
			
			byte opcode = (byte)data.ReadByte(ref pos);
			// kcp heartbeat
			if (opcode == 200)
				return;
			// DebugLogger.Log($"[SnipeClient] : Received opcode {opcode}");

			// auth request -> auth response -> authenticated
			// handled in OnUdpClientConnected
			if (opcode == OPCODE_AUTHENTICATION_REQUEST)
			{
				return;
			}
			else if (opcode == OPCODE_AUTHENTICATED)
			{
				UdpClientId = data.ReadInt(ref pos);
				
				DebugLogger.Log($"[SnipeClient] UdpClientId = {UdpClientId}");
				
				OnConnected();
			}
			else if (opcode == OPCODE_SNIPE_RESPONSE)
			{
				//var len = data.ReadInt(ref posin);
				// DebugLogger.Log($"[SnipeClient] recv snipe response"); // ({len} bytes) "); // {BitConverter.ToString(b.Array, posin, len)}
				byte[] msg = data.ReadBytes(ref pos);
				ProcessMessage(msg);
			}
		}
		
		private void DoSendRequestUdpClient(byte[] msg)
		{
			int pos = 0;
			// opcode + length (4 bytes) + msg
			byte[] data = new byte[msg.Length + 5];
			data.WriteByte(ref pos, OPCODE_SNIPE_REQUEST);
			data.WriteBytes(ref pos, msg);
			mUdpClient.Send(new ArraySegment<byte>(data, 0, pos), KcpChannel.Reliable);
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
		
		#endregion UdpClient
		
		
	}
}