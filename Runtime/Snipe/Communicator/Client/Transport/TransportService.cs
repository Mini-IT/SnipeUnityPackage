using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	internal sealed class TransportService : IDisposable
	{
		private readonly SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;
		private readonly TransportFactory _transportFactory;
		private readonly List<Func<Transport>> _transportFactoriesQueue = new List<Func<Transport>>(3);
		private int _currentTransportIndex;

		private Transport _currentTransport;
		private TimeSpan _prevDisconnectTime = TimeSpan.Zero;
		private long _connectionStartTimestamp;

		public bool Connected => _currentTransport != null && _currentTransport.Connected;
		public bool WebSocketConnected => Connected && _currentTransport is WebSocketTransport;
		public bool UdpClientConnected => Connected && _currentTransport is KcpTransport;
		public bool HttpClientConnected => Connected && _currentTransport is HttpTransport;

		public event Action<Transport> ConnectionOpened;
		public event Action<Transport> ConnectionClosed;
		public event Action<Transport> ConnectionDisrupted;
		public event Action UdpConnectionFailed;
		public event Action<IDictionary<string, object>> MessageReceived;

		internal TransportService(SnipeConfig config, SnipeAnalyticsTracker analytics)
		{
			_config = config;
			_analytics = analytics;
			_transportFactory = new TransportFactory(config, analytics);
		}

		public Transport GetCurrentTransport() => _currentTransport;

		public void InitializeTransports()
		{
			_currentTransportIndex = 0;

			if (_transportFactoriesQueue.Count > 0)
			{
				return;
			}

#if !UNITY_WEBGL
			if (_config.CheckUdpAvailable())
			{
				_transportFactoriesQueue.Add(() => _transportFactory.CreateKcpTransport(OnTransportOpened, OnTransportClosed, OnMessageReceived));
			}
#endif

			if (_config.CheckWebSocketAvailable())
			{
				_transportFactoriesQueue.Add(() => _transportFactory.CreateWebSocketTransport(OnTransportOpened, OnTransportClosed, OnMessageReceived));
			}

			if (_config.CheckHttpAvailable())
			{
				_transportFactoriesQueue.Add(() => _transportFactory.CreateHttpTransport(OnTransportOpened, OnTransportClosed, OnMessageReceived));
			}
		}

		public bool TryStartNextTransport()
		{
			if (_currentTransport != null && _currentTransport.Started)
			{
				return false;
			}

			StopCurrentTransport(false);

			if (_currentTransportIndex >= _transportFactoriesQueue.Count - 1)
			{
				return false;
			}

			var factory = _transportFactoriesQueue[_currentTransportIndex];
			_currentTransportIndex++;

			_currentTransport = factory.Invoke();

			_connectionStartTimestamp = Stopwatch.GetTimestamp();
			_prevDisconnectTime = TimeSpan.Zero;
			_currentTransport.Connect();

			return true;

		}

		public void StopCurrentTransport(bool raiseEvent)
		{
			if (_currentTransport != null)
			{
				_currentTransport.Dispose();
				_currentTransport = null;
			}

			_analytics.PingTime = TimeSpan.Zero;
			_analytics.ServerReaction = TimeSpan.Zero;

			if (raiseEvent)
			{
				ConnectionClosed?.Invoke(null);
			}
		}

		public void SetLoggedIn()
		{
			if (_currentTransport is WebSocketTransport webSocketTransport)
			{
				webSocketTransport.SetLoggedIn(true);
			}
		}

		public void SendMessage(IDictionary<string, object> message)
		{
			_currentTransport?.SendMessage(message);
		}

		public void SendBatch(List<IDictionary<string, object>> messages)
		{
			_currentTransport?.SendBatch(messages);
		}

		public TransportInfo GetTransportInfo() => _currentTransport?.Info ?? default;

		public void Dispose()
		{
			StopCurrentTransport(false);
		}

		private void OnTransportOpened(Transport transport)
		{
			if (transport != _currentTransport)
			{
				return;
			}

			_analytics.ConnectionEstablishmentTime = StopwatchUtil.GetElapsedTime(_connectionStartTimestamp);
			ConnectionOpened?.Invoke(transport);
		}

		private void OnTransportClosed(Transport transport)
		{
			if (transport != _currentTransport)
			{
				return;
			}

			if (transport is KcpTransport)
			{
				UdpConnectionFailed?.Invoke();
			}

			// If disconnected twice during 10 seconds, then force transport change
			TimeSpan now = DateTimeOffset.UtcNow.Offset;
			TimeSpan dif = now - _prevDisconnectTime;
			_prevDisconnectTime = now;

			if (transport.ConnectionVerified && dif.TotalSeconds > 10)
			{
				StopCurrentTransport(true);
			}
			else // Not connected yet or connection is lossy. Try another transport
			{
				StopCurrentTransport(false);

				if (!TryStartNextTransport())
				{
					StopCurrentTransport(true);
				}
			}

			ConnectionDisrupted?.Invoke(transport);
		}

		private void OnMessageReceived(IDictionary<string, object> message)
		{
			MessageReceived?.Invoke(message);
		}
	}
}
