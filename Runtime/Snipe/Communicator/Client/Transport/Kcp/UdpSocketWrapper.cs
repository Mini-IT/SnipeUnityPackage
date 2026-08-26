using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public sealed class UdpSocketWrapper : IDisposable
	{
		public bool IsConnected => _socket != null && _socket.Connected;

		private Socket _socket;
		private int _connectAttempt;

		private readonly ILogger _logger;
		private readonly object _connectLock = new object();
		private readonly Action _connectedHandler;
		private readonly Action _disconnectedHandler;

		public UdpSocketWrapper(ISnipeServices services, Action connectedHandler, Action disconnectedHandler)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			_connectedHandler = connectedHandler;
			_disconnectedHandler = disconnectedHandler;

			_logger = services.LoggerFactory.CreateLogger(nameof(UdpSocketWrapper));
		}

		public void Connect(string host, ushort port, int millisecondsTimeout)
		{
			int attempt;
			lock (_connectLock)
			{
				attempt = ++_connectAttempt;
			}

			_ = ConnectAsync(host, port, attempt);
			_ = WaitForConnectTimeout(millisecondsTimeout, attempt);
		}

		private async Task ConnectAsync(string host, ushort port, int attempt)
		{
			_logger.LogTrace($"connect to {host}:{port}");

			Socket socket = null;

			try
			{
				IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
				if (!IsConnectAttemptActive(attempt))
				{
					return;
				}

				socket = await ConnectSocket(addresses, port, attempt);
			}
			catch (Exception e)
			{
				_logger.LogTrace($"Failed to connect to {host}:{port}: {e.Message}");
			}

			if (socket != null && CompleteConnect(attempt, socket))
			{
				_connectedHandler?.Invoke();
				return;
			}

			socket?.Close();
			FailConnect(attempt);
		}

		private async Task WaitForConnectTimeout(int millisecondsTimeout, int attempt)
		{
			await Task.Delay(millisecondsTimeout);
			if (FailConnect(attempt))
			{
				_logger.LogTrace("UDP connect timed out after {timeout}ms", millisecondsTimeout);
			}
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
				_disconnectedHandler?.Invoke();
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
			Socket socket;
			lock (_connectLock)
			{
				_connectAttempt++;
				socket = _socket;
				_socket = null;
			}

			socket?.Close();
		}

		// https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket?view=netframework-4.8#examples
		private async Task<Socket> ConnectSocket(IPAddress[] addresses, int port, int attempt)
		{
			// Loop through the AddressList to obtain the supported AddressFamily. This is to avoid
			// an exception that occurs when the host IP Address is not compatible with the address family
			// (typical in the IPv6 case).
			foreach (IPAddress address in addresses)
			{
				if (!IsConnectAttemptActive(attempt))
				{
					return null;
				}

				IPEndPoint ipe = new IPEndPoint(address, port);
				Socket socket = new Socket(ipe.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
				try
				{
					await socket.ConnectAsync(ipe);
				}
				catch (Exception)
				{
					socket.Close();
					continue;
				}

				if (!IsConnectAttemptActive(attempt) || !socket.Connected)
				{
					socket.Close();
					continue;
				}

				if (socket.Connected)
				{
					return socket;
				}
			}
			return null;
		}

		private bool CompleteConnect(int attempt, Socket socket)
		{
			lock (_connectLock)
			{
				if (_connectAttempt != attempt)
				{
					return false;
				}

				_connectAttempt++;
				_socket = socket;
				return true;
			}
		}

		private bool FailConnect(int attempt)
		{
			lock (_connectLock)
			{
				if (_connectAttempt != attempt)
				{
					return false;
				}

				_connectAttempt++;
			}

			_disconnectedHandler?.Invoke();
			return true;
		}

		private bool IsConnectAttemptActive(int attempt)
		{
			lock (_connectLock)
			{
				return _connectAttempt == attempt;
			}
		}
	}
}
