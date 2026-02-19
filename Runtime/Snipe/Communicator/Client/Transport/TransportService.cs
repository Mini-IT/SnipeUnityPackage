using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	internal sealed class TransportService : IDisposable
	{
		private sealed class TransportEntry
		{
			public Transport Instance;
			public Func<Transport> Factory;
			public Func<(string endpoint, ushort port)> ResolveEndpoint;
			public Func<bool> TryAdvanceUrl;
		}

		private readonly SnipeOptions _options;
		private readonly IAnalyticsContext _analytics;
		private readonly TransportFactory _transportFactory;
		private readonly List<TransportEntry> _transportEntries = new List<TransportEntry>(3);
		private int _currentTransportIndex;
		private bool _retryCurrentTransport;

		private Transport _currentTransport;
		private TransportInfo _currentTransportInfo;
		private long _connectionStartTimestamp;

		public bool Connected => _currentTransport != null && _currentTransport.Connected;
		public bool WebSocketConnected => Connected && _currentTransport is WebSocketTransport;
		public bool UdpClientConnected => Connected && _currentTransport is KcpTransport;
		public bool HttpClientConnected => Connected && _currentTransport is HttpTransport;

		public event Action<Transport> ConnectionOpened;
		public event Action<TransportInfo> ConnectionClosed;
		public event Action<Transport> ConnectionDisrupted;
		public event Action UdpConnectionFailed;
		public event Action<IDictionary<string, object>> MessageReceived;

		internal TransportService(SnipeOptions options, IAnalyticsContext analytics, ISnipeServices services)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			_options = options;
			_analytics = analytics;
			_transportFactory = new TransportFactory(options, analytics, services);
		}

		public Transport GetCurrentTransport() => _currentTransport;

		public void InitializeTransports()
		{
			_currentTransportIndex = -1;
			_retryCurrentTransport = false;

			if (_transportEntries.Count > 0)
			{
				return;
			}

#if !UNITY_WEBGL
			if (_options.CheckUdpAvailable())
			{
				_transportEntries.Add(new TransportEntry
				{
					Factory = () => _transportFactory.CreateKcpTransport(OnTransportOpened, OnTransportClosed, OnMessageReceived),
					ResolveEndpoint = () =>
					{
						var address = _options.GetUdpAddress();
						return address == null ? (null, 0) : (address.Host, address.Port);
					},
					TryAdvanceUrl = () => _options.NextUdpUrl()
				});
			}
#endif

			if (_options.CheckWebSocketAvailable())
			{
				_transportEntries.Add(new TransportEntry
				{
					Factory = () => _transportFactory.CreateWebSocketTransport(OnTransportOpened, OnTransportClosed, OnMessageReceived),
					ResolveEndpoint = () => (_options.GetWebSocketUrl(), 0),
					TryAdvanceUrl = () => _options.NextWebSocketUrl()
				});
			}

			if (_options.CheckHttpAvailable())
			{
				_transportEntries.Add(new TransportEntry
				{
					Factory = () => _transportFactory.CreateHttpTransport(OnTransportOpened, OnTransportClosed, OnMessageReceived),
					ResolveEndpoint = () => (_options.GetHttpAddress(), 0),
					TryAdvanceUrl = () => _options.NextHttpUrl()
				});
			}
		}

		public bool TryStartNextTransport()
		{
			if (_transportEntries.Count == 0)
			{
				return false;
			}

			if (_currentTransport != null && _currentTransport.Started)
			{
				return false;
			}

			while (true)
			{
				var entry = GetEntryToStart();
				if (entry == null)
				{
					return false;
				}

				if (entry.Instance == null)
				{
					entry.Instance = entry.Factory.Invoke();
				}

				_currentTransport = entry.Instance;

				var (endpoint, port) = entry.ResolveEndpoint();
				if (string.IsNullOrEmpty(endpoint) || (_currentTransport is KcpTransport && port == 0))
				{
					DisposeEntry(entry);
					continue;
				}

				_connectionStartTimestamp = Stopwatch.GetTimestamp();
				_currentTransport.Connect(endpoint, port);

				return true;
			}
		}

		public void StopCurrentTransport()
		{
			if (_currentTransport != null)
			{
				_currentTransportInfo = _currentTransport.Info;
				_currentTransport.Dispose();
				_currentTransport = null;
			}

			DisposeEntries();
			_currentTransportIndex = -1;
			_retryCurrentTransport = false;

			ResetAnalyticsMetrics();
		}

		public void RaiseConnectionClosedEvent()
		{
			ConnectionClosed?.Invoke(_currentTransportInfo);
			_currentTransportInfo = default;
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
			StopCurrentTransport();
			_transportEntries.Clear();
		}


		private TransportEntry GetEntryToStart()
		{
			if (_retryCurrentTransport)
			{
				_retryCurrentTransport = false;
				var current = GetCurrentEntry();
				if (current != null)
				{
					return current;
				}
			}

			if (!MoveToNextEntry())
			{
				return null;
			}

			return GetCurrentEntry();
		}

		private bool MoveToNextEntry()
		{
			int nextIndex = _currentTransportIndex;
			while (true)
			{
				nextIndex++;
				if (nextIndex >= _transportEntries.Count)
				{
					return false;
				}

				if (_transportEntries[nextIndex] != null)
				{
					_currentTransportIndex = nextIndex;
					return true;
				}
			}
		}

		private TransportEntry GetCurrentEntry()
		{
			if (_currentTransportIndex < 0 || _currentTransportIndex >= _transportEntries.Count)
			{
				return null;
			}

			return _transportEntries[_currentTransportIndex];
		}


		private void DisposeEntry(TransportEntry entry)
		{
			if (entry?.Instance == null)
			{
				return;
			}

			entry.Instance.Dispose();

			if (_currentTransport == entry.Instance)
			{
				_currentTransport = null;
			}

			entry.Instance = null;
		}

		private void DisposeEntries()
		{
			foreach (var entry in _transportEntries)
			{
				DisposeEntry(entry);
			}
		}

		private void ResetAnalyticsMetrics()
		{
			_analytics.PingTime = TimeSpan.Zero;
			_analytics.ServerReaction = TimeSpan.Zero;
		}

		private void FinishConnectionAttempts()
		{
			DisposeEntries();
			_currentTransport = null;
			_currentTransportIndex = -1;
			_retryCurrentTransport = false;
			ResetAnalyticsMetrics();
			RaiseConnectionClosedEvent();
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

			_currentTransportInfo = transport.Info;
			_currentTransport = null;
			ResetAnalyticsMetrics();

			bool hasMoreUrls = false;

			var entry = GetCurrentEntry();
			if (entry != null)
			{
				hasMoreUrls = entry.TryAdvanceUrl();
				if (!hasMoreUrls)
				{
					DisposeEntry(entry);
				}
			}

			ConnectionDisrupted?.Invoke(transport);

			_retryCurrentTransport = hasMoreUrls;

			if (!TryStartNextTransport())
			{
				FinishConnectionAttempts();
			}
		}

		private void OnMessageReceived(IDictionary<string, object> message)
		{
			MessageReceived?.Invoke(message);
		}
	}
}
