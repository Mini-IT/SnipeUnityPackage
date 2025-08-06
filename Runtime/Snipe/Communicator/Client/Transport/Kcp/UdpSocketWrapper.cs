using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe
{
	public sealed class UdpSocketWrapper : IDisposable
	{
		public Action OnConnected;
		public Action OnDisconnected;

		public bool Connected => _socket != null && _socket.Connected;

		private Socket _socket;

		private readonly ILogger _logger;

		public UdpSocketWrapper()
		{
			_logger = SnipeServices.LogService.GetLogger(nameof(UdpSocketWrapper));
		}

		public async void Connect(string host, ushort port)
		{
			_logger.LogTrace($"connect to {host}:{port}");

			IPAddress[] addresses;

			try
			{
				addresses = await Dns.GetHostAddressesAsync(host);
			}
			catch (SocketException)
			{
				_logger.LogTrace($"Failed to resolve host: {host}");
				addresses = null;
			}

			if (addresses != null && addresses.Length > 0)
			{
				_socket = await ConnectSocket(addresses, port);

				if (_socket != null)
				{
					OnConnected?.Invoke();
					return;
				}
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
			catch (Exception)
			{
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
				catch (Exception)
				{
					socket = null;
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
