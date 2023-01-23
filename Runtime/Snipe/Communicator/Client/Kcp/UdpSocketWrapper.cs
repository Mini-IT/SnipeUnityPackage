using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class UdpSocketWrapper : IDisposable
	{
		public Action OnConnected;
		public Action OnDisconnected;

		public bool Connected => _socket != null && _socket.Connected;

		private Socket _socket;

		public async void Connect(string host, ushort port)
		{
			DebugLogger.Log($"[UdpSocketWrapper] connect to {host}:{port}");
			
			Analytics.ConnectionUrl = $"{host}:{port}";
			Analytics.UdpException = null;
			Analytics.TrackSocketStartConnection("UdpSocketWrapper");

			var stopwatch = Stopwatch.StartNew();

			IPAddress[] addresses;

			try
			{
				addresses = await Dns.GetHostAddressesAsync(host);
			}
			catch (SocketException)
			{
				DebugLogger.Log($"[UdpSocketWrapper] Failed to resolve host: {host}");
				addresses = null;
			}

			Analytics.UdpDnsResolveTime = stopwatch.Elapsed;
			stopwatch.Restart();

			if (addresses != null && addresses.Length > 0)
			{
				Analytics.UdpDnsResolved = true;

				_socket = await ConnectSocket(addresses, port);
				
				Analytics.UdpSocketConnectTime = stopwatch.Elapsed;

				if (_socket != null)
				{
					OnConnected?.Invoke();
					return;
				}
			}
			else
			{
				Analytics.UdpDnsResolved = false;
			}

			OnDisconnected?.Invoke();
		}

		public void Send(byte[] data, int length)
		{
			if (_socket == null)
				return;
			
			try
			{
				_socket.Send(data, length, SocketFlags.None);
			}
			catch (Exception e)
			{
				Analytics.UdpException = e;
				Dispose();
				OnDisconnected?.Invoke();
			}
		}

		public int Receive(byte[] buffer)
		{
			if (_socket != null)
			{
				return _socket.Receive(buffer);
			}
			return 0;
		}

		public bool Poll(int microSeconds, SelectMode mode)
		{
			if (_socket != null)
			{
				return _socket.Poll(microSeconds, mode);
			}
			return false;
		}

		public void Dispose()
		{
			if (_socket != null)
			{
				_socket.Close();
				_socket = null;
			}
		}

		// https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket?view=netframework-4.8#examples
		private async Task<Socket> ConnectSocket(IPAddress[] addresses, int port)
		{
			// Loop through the AddressList to obtain the supported AddressFamily. This is to avoid
			// an exception that occurs when the host IP Address is not compatible with the address family
			// (typical in the IPv6 case).
			foreach (IPAddress address in addresses)
			{
				IPEndPoint ipe = new IPEndPoint(address, port);
				Socket socket = new Socket(ipe.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
				try
				{
					await socket.ConnectAsync(ipe);
				}
				catch (Exception e)
				{
					socket = null;
					Analytics.UdpException = e;
				}

				if (socket != null && socket.Connected)
				{
					return socket;
				}
			}
			return null;
		}
	}
}