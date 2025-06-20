using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public class SnipeClient : IDisposable
	{
		public const int SNIPE_VERSION = 6;
		public const int MAX_BATCH_SIZE = 5;

		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;
		public event Action UdpConnectionFailed;

		// KLUDGE: Needed for clearing batched requests on disconnect during login
		[Obsolete("Will be removed in v.8")]
		public event Action InternalConnectionClosed;

		private Transport _transport;

		private bool _loggedIn = false;

		public bool Connected => _transport != null && _transport.Connected;
		public bool LoggedIn { get { return _loggedIn && Connected; } }
		public bool WebSocketConnected => Connected && _transport is WebSocketTransport;
		public bool UdpClientConnected => Connected && _transport is KcpTransport;
		public bool HttpClientConnected => Connected && _transport is HttpTransport;

		public string ConnectionId { get; private set; }

		private long _connectionStartTimestamp;

		private long _serverReactionStartTimestamp;
		public TimeSpan CurrentRequestElapsed => GetElapsedTime(_serverReactionStartTimestamp);

		public bool BatchMode
		{
			get => _batchedRequests != null;
			set
			{
				if (value == BatchMode)
				{
					return;
				}

				if (value)
				{
					_batchedRequests ??= new ConcurrentQueue<SnipeObject>();
				}
				else
				{
					FlushBatchedRequests();
					_batchedRequests = null;
				}

				_logger.LogTrace($"BatchMode = {value}");
			}
		}

		private ConcurrentQueue<SnipeObject> _batchedRequests;
		private readonly object _batchLock = new object();

		private readonly Queue<Func<Transport>> _transportFactoriesQueue = new Queue<Func<Transport>>(3);

		private int _requestId = 0;
		private TimeSpan _prevDisconnectTime = TimeSpan.Zero;

		private readonly SnipeConfig _config;
		private readonly SnipeAnalyticsTracker _analytics;
		private readonly ResponseMonitor _responseMonitor;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly ILogger _logger;

		internal SnipeClient(SnipeConfig config)
		{
			_config = config;
			_analytics = SnipeServices.Analytics.GetTracker(config.ContextId);
			_responseMonitor = new ResponseMonitor(_analytics);
			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_logger = SnipeServices.LogService.GetLogger(nameof(SnipeClient));
		}

		public void Connect()
		{
			_transportFactoriesQueue.Clear();

#if !UNITY_WEBGL
			if (_config.CheckUdpAvailable())
			{
				_transportFactoriesQueue.Enqueue(CreateKcpTransport);
			}
#endif

			if (_config.CheckWebSocketAvailable())
			{
				_transportFactoriesQueue.Enqueue(CreateWebSocketTransport);
			}

			if (_config.CheckHttpAvailable())
			{
				_transportFactoriesQueue.Enqueue(CreateHttpTransport);
			}

			StartNextTransport();
		}

		public TransportInfo GetTransportInfo() => _transport?.Info ?? default;

		private bool StartNextTransport()
		{
#if NET_STANDARD_2_1
			if (_transportFactoriesQueue.TryDequeue(out var transportFactory))
			{
#else
			if (_transportFactoriesQueue.Count > 0)
			{
				var transportFactory = _transportFactoriesQueue.Dequeue();
#endif
				StartTransport(transportFactory);
				return true;
			}

			return false;
		}

		private void StartTransport(Func<Transport> transportFactory)
		{
			if (_transport != null && _transport.Started)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			_transport ??= transportFactory.Invoke();

			_connectionStartTimestamp = Stopwatch.GetTimestamp();
			_prevDisconnectTime = TimeSpan.Zero;
			_transport.Connect();
		}

		#region Transport factories

		private KcpTransport CreateKcpTransport()
		{
			var transport = new KcpTransport(_config, _analytics);

			transport.ConnectionOpenedHandler = (Transport t) =>
			{
				_analytics.UdpConnectionTime = GetElapsedTime(_connectionStartTimestamp);
				OnTransportConnectionOpened(t);
			};

			transport.ConnectionClosedHandler = (Transport t) =>
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					UdpConnectionFailed?.Invoke();
				});

				OnTransportConnectionClosed(t);
			};

			transport.MessageReceivedHandler = ProcessMessage;

			return transport;
		}

		private WebSocketTransport CreateWebSocketTransport()
		{
			return new WebSocketTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = OnTransportConnectionOpened,
				ConnectionClosedHandler = OnTransportConnectionClosed,
				MessageReceivedHandler = ProcessMessage
			};
		}

		private HttpTransport CreateHttpTransport()
		{
			return new HttpTransport(_config, _analytics)
			{
				ConnectionOpenedHandler = OnTransportConnectionOpened,
				ConnectionClosedHandler = OnTransportConnectionClosed,
				MessageReceivedHandler = ProcessMessage,
			};
		}

		#endregion

		private void OnTransportConnectionOpened(Transport transport)
		{
			_analytics.ConnectionEstablishmentTime = GetElapsedTime(_connectionStartTimestamp);

			_mainThreadRunner.RunInMainThread(() =>
			{
				try
				{
					ConnectionOpened?.Invoke();
				}
				catch (Exception e)
				{
					_logger.LogTrace("ConnectionOpened invocation error: {0}", e);
					_analytics.TrackError("ConnectionOpened invocation error", e);
				}
			});
		}

		private void OnTransportConnectionClosed(Transport transport)
		{
			if (transport != _transport)
			{
				return;
			}

			// If disconnected twice during 10 seconds, then force transport change
			TimeSpan now = DateTimeOffset.UtcNow.Offset;
			TimeSpan dif = now - _prevDisconnectTime;
			_prevDisconnectTime = now;

			if (transport.ConnectionVerified && dif.TotalSeconds > 10)
			{
				Disconnect(true);
			}
			else // Not connected yet or connection is lossy. Try another transport
			{
				Disconnect(false); // stop the transport and clean up

				bool started = StartNextTransport();
				if (!started)
				{
					Disconnect(true);
				}
			}
		}

		private void RaiseConnectionClosedEvent()
		{
			_mainThreadRunner.RunInMainThread(() =>
			{
				try
				{
					ConnectionClosed?.Invoke();
				}
				catch (Exception e)
				{
					_logger.LogTrace("ConnectionClosed invocation error: {0}", e);
					_analytics.TrackError("ConnectionClosed invocation error", e);
				}
			});
		}

		public void Disconnect()
		{
			Disconnect(true);
		}

		private void Disconnect(bool raiseEvent)
		{
			_loggedIn = false;
			ConnectionId = "";

			_analytics.PingTime = TimeSpan.Zero;
			_analytics.ServerReaction = TimeSpan.Zero;

			_responseMonitor.Stop();

			if (_transport != null)
			{
				_transport.Dispose();
				_transport = null;
			}

			// KLUDGE: Needed for clearing batched requests on disconnect during login
			// To be removed in v.8
			if (InternalConnectionClosed != null)
			{
				_mainThreadRunner.RunInMainThread(() => InternalConnectionClosed?.Invoke());
			}

			if (raiseEvent)
			{
				RaiseConnectionClosedEvent();
			}
		}

		public int SendRequest(string messageType, SnipeObject data)
		{
			if (!Connected)
			{
				return 0;
			}

			var message = new SnipeObject() { ["t"] = messageType };

			if (data != null)
			{
				message.Add("data", data);
			}

			return SendRequest(message);
		}

		public int SendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
			{
				return 0;
			}

			int requestId = ++_requestId;
			message["id"] = requestId;

			SnipeObject data = null;

			if (!_loggedIn && (_batchedRequests == null || _batchedRequests.IsEmpty))
			{
				data = message["data"] as SnipeObject ?? new SnipeObject();
				data["ckey"] = _config.ClientKey;
				message["data"] = data;
			}

			if (_config.DebugId != null)
			{
				data ??= message["data"] as SnipeObject ?? new SnipeObject();
				data["debugID"] = _config.DebugId;
			}

			if (BatchMode)
			{
				_batchedRequests!.Enqueue(message);

				if (_batchedRequests.Count >= MAX_BATCH_SIZE)
				{
					FlushBatchedRequests();
				}
			}
			else
			{
				DoSendRequest(message);
			}

			return requestId;
		}

		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
			{
				return;
			}

			if (_logger.IsEnabled(LogLevel.Trace))
			{
				_logger.LogTrace("SendRequest - {0}", message.ToJSONString());
			}

			_transport.SendMessage(message);

			_serverReactionStartTimestamp = Stopwatch.GetTimestamp();

			int id = message.SafeGetValue<int>("id");
			_responseMonitor.Add(id, message.SafeGetString("t"));
		}

		private void DoSendBatch(List<SnipeObject> messages)
		{
			if (!Connected || messages == null || messages.Count == 0)
				return;

			_logger.LogTrace("DoSendBatch - {0} items", messages.Count);

			_transport.SendBatch(messages);
		}

		private void ProcessMessage(SnipeObject message)
		{
			if (message == null)
			{
				return;
			}

			if (_serverReactionStartTimestamp > 0)
			{
				_analytics.ServerReaction = GetElapsedTime(_serverReactionStartTimestamp);
			}

			string messageType = message.SafeGetString("t");
			string errorCode = message.SafeGetString("errorCode");
			int requestID = message.SafeGetValue<int>("id");
			int ackID = message.SafeGetValue<int>("ackID");
			SnipeObject responseData = message.SafeGetValue<SnipeObject>("data");

			_responseMonitor.Remove(requestID, messageType);

			if (_logger.IsEnabled(LogLevel.Trace))
			{
				_logger.LogTrace("[{0}] ProcessMessage - {1} - {2} {3} {4}", ConnectionId, requestID, messageType, errorCode, responseData?.ToFastJSONString());
			}

			if (!_loggedIn && messageType == SnipeMessageTypes.USER_LOGIN)
			{
				ProcessLoginResponse(errorCode, responseData);
			}

			InvokeMessageReceived(messageType, errorCode, requestID, responseData);

			if (ackID > 0)
			{
				SendRequest("ack.ack", new SnipeObject() { ["ackID"] = ackID });
			}
		}

		private void ProcessLoginResponse(string errorCode, SnipeObject responseData)
		{
			if (errorCode == SnipeErrorCodes.OK || errorCode == SnipeErrorCodes.ALREADY_LOGGED_IN)
			{
				_logger.LogTrace("[{0}] ProcessMessage - Login Succeeded", ConnectionId);

				_loggedIn = true;

				if (_transport is WebSocketTransport webSocketTransport)
				{
					webSocketTransport.SetLoggedIn(true);
				}

				if (responseData != null)
				{
					ConnectionId = responseData.SafeGetString("connectionID");
				}
				else
				{
					ConnectionId = "";
				}

				if (LoginSucceeded != null)
				{
					_mainThreadRunner.RunInMainThread(() =>
					{
						try
						{
							LoginSucceeded?.Invoke();
						}
						catch (Exception e)
						{
							_logger.LogTrace("[{0}] ProcessMessage - LoginSucceeded invocation error: {1}", ConnectionId, e);
							_analytics.TrackError("LoginSucceeded invocation error", e);
						}
					});
				}
			}
			else
			{
				_logger.LogTrace("[{0}] ProcessMessage - Login Failed", ConnectionId);

				if (LoginFailed != null)
				{
					_mainThreadRunner.RunInMainThread(() =>
					{
						try
						{
							LoginFailed?.Invoke(errorCode);
						}
						catch (Exception e)
						{
							_logger.LogTrace("[{0}] ProcessMessage - LoginFailed invocation error: {1}", ConnectionId, e);
							_analytics.TrackError("LoginFailed invocation error", e);
						}
					});
				}
			}
		}

		private void InvokeMessageReceived(string messageType, string errorCode, int requestId, SnipeObject responseData)
		{
			if (MessageReceived != null)
			{
				_mainThreadRunner.RunInMainThread(() =>
				{
					try
					{
						MessageReceived?.Invoke(messageType, errorCode, responseData, requestId);
					}
					catch (Exception e)
					{
						_logger.LogTrace("[{0}] ProcessMessage - {1} - MessageReceived invocation error: {2}", ConnectionId, messageType, e);
						_analytics.TrackError("MessageReceived invocation error", e, new Dictionary<string, object>()
						{
							["messageType"] = messageType,
							["errorCode"] = errorCode,
						});
					}
				});
			}
			else
			{
				_logger.LogTrace("[{0}] ProcessMessage - no MessageReceived listeners", ConnectionId);
			}
		}

		private void FlushBatchedRequests()
		{
			ReadOnlySpan<SnipeObject> queue;

			lock (_batchLock)
			{
				if (_batchedRequests == null || _batchedRequests.IsEmpty)
				{
					return;
				}

				// local copy for thread safety
				queue = _batchedRequests.ToArray();
				_batchedRequests.Clear();
			}

			if (queue.Length == 1)
			{
				DoSendRequest(queue[0]);
			}
			else
			{
				var messages = new List<SnipeObject>(queue.Length);

				for (int i = 0; i < queue.Length; i++)
				{
					var message = queue[i];
					messages.Add(message);
					_logger.LogTrace("Request batched - {0}", message.ToJSONString());
				}

				DoSendBatch(messages);
			}
		}

		// Stopwatch.GetElapsedTime() is added only in .NET 7+
		// Here is a custom implementation
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static TimeSpan GetElapsedTime(long startTS)
		{
			return (startTS > 0) ? TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTS) : TimeSpan.Zero;
		}

		public void Dispose()
		{
			Disconnect(false);
			_responseMonitor.Dispose();
		}
	}
}
