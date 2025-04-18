using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Threading;

namespace MiniIT.Snipe
{
	public class HttpTransport : Transport
	{
		private const string API_PATH = "api/v1/request/";

		private static readonly Dictionary<string, object> s_pingMessage = new () { ["t"] = "server.ping", ["id"] = -1 };

		public override bool Started => _started;
		public override bool Connected => _connected;
		public override bool ConnectionEstablished => _connectionEstablished;

		public bool IntensiveHeartbeat
		{
			get => _intensiveHeartbeat;
			set
			{
				if (_intensiveHeartbeat == value)
					return;

				_intensiveHeartbeat = value;
				UpdateHeartbeat();
			}
		}

		private IHttpClient _client;

		private CancellationTokenSource _heartbeatCancellation;
		private bool _heartbeatRunning = false;
		private bool _intensiveHeartbeat = false;
		private TimeSpan _heartbeatInterval;
		private readonly TimeSpan _heartbeatIntensiveInterval = TimeSpan.FromSeconds(1);

		private Uri _baseUrl;
		private bool _started;
		private bool _connected;
		private bool _connectionEstablished = false;
		private readonly object _lock = new object();

		private readonly AlterSemaphore _sendSemaphore = new AlterSemaphore(1, 1);

		internal HttpTransport(SnipeConfig config, SnipeAnalyticsTracker analytics)
			: base(config, analytics)
		{
		}

		public override void Connect()
		{
			lock (_lock)
			{
				if (Started)
				{
					return;
				}
				_started = true;

				_baseUrl = GetBaseUrl();

				_client ??= SnipeServices.HttpClientFactory.CreateHttpClient();

				Info = new TransportInfo()
				{
					Protocol = TransportProtocol.Http,
					ClientImplementation = _client.GetType().Name,
				};
			}

			SendHandshake();
		}

		public override void Disconnect()
		{
			if (!_connected)
			{
				return;
			}

			InternalDisconnect();
		}

		private void InternalDisconnect()
		{
			_connected = false;
			_started = false;

			StopHeartbeat();

			_client?.Reset();

			ConnectionClosedHandler?.Invoke(this);

			// It's important to keep the value during ConnectionClosedHandler invocation
			_connectionEstablished = false;
		}

		public override void SendMessage(IDictionary<string, object> message)
		{
			DoSendRequest(message);
		}

		public override void SendBatch(IList<IDictionary<string, object>> messages)
		{
			if (messages.Count == 1)
			{
				DoSendRequest(messages[0]);
				return;
			}

			DoSendBatch(messages);
		}

		private Uri GetBaseUrl()
		{
			string url = _config.GetHttpAddress();

			if (!url.EndsWith("/"))
			{
				url += "/";
			}
			url += API_PATH;

			return new Uri(url);
		}

		private void OnClientConnected()
		{
			_logger.LogTrace("OnClientConnected");

			ConnectionOpenedHandler?.Invoke(this);
		}

		private void ProcessMessage(string json)
		{
			var message = JsonUtility.ParseDictionary(json);

			if (message == null)
			{
				_logger.LogError("ProcessMessage: message is null. Json = " + json);
				return;
			}

			if (message.SafeGetString("t") == "server.responses")
			{
				if (message.TryGetValue("data", out var innerData))
				{
					if (innerData is IList innerMessages)
					{
						ProcessBatchInnerMessages(innerMessages);
					}
					else if (innerData is IDictionary<string, object> dataDict &&
					         dataDict.TryGetValue("list", out var dataList) &&
					         dataList is IList innerList)
					{
						ProcessBatchInnerMessages(innerList);
					}
				}
			}
			else // single message
			{
				InternalProcessMessage(message);
			}
		}

		private void ProcessBatchInnerMessages(IList innerMessages)
		{
			foreach (var innerMessage in innerMessages)
			{
				if (innerMessage is IDictionary<string, object> message)
				{
					InternalProcessMessage(message);
				}
			}
		}

		private void InternalProcessMessage(IDictionary<string, object> message)
		{
			ExtractAuthToken(message);
			MessageReceivedHandler?.Invoke(message);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ExtractAuthToken(IDictionary<string, object> message)
		{
			_logger.LogTrace(JsonUtility.ToJson(message));

			if (message.SafeGetString("t") == "user.login")
			{
				if (!message.TryGetValue("token", out string token))
				{
					if (message.TryGetValue<IDictionary<string, object>>("data", out var data))
					{
						data.TryGetValue("token", out token);
					}
				}

				if (!string.IsNullOrEmpty(token))
				{
					_client.SetAuthToken(token);
				}
			}
		}

		private async void DoSendRequest(IDictionary<string, object> message)
		{
			string requestType = message.SafeGetString("t");
			int requestId = message.SafeGetValue<int>("id");
			string json = JsonUtility.ToJson(message);

			string responseMessage = null;

			bool semaphoreOccupied = false;

			try
			{
				await _sendSemaphore.WaitAsync();
				semaphoreOccupied = true;

				// During awaiting the semaphore a disconnect could happen - check it
				if (!_connected)
				{
					_sendSemaphore.Release();
					return;
				}

				var uri = new Uri(_baseUrl, requestType);

				_logger.LogTrace($"<<< request ({uri}) - {requestId} - {requestType} - {json}");

				using (var response = await _client.PostJson(uri, json))
				{
					// response.StatusCode:
					//   200 - ok
					//   401 - wrong auth token,
					//   429 - {"errorCode":"rateLimit"}
					//   500 - {"errorCode":"requestTimeout"}

					_logger.LogTrace($">>> response - {requestId} - {requestType} ({response.ResponseCode}) {response.Error}");

					if (response.IsSuccess)
					{
						responseMessage = await response.GetStringContentAsync();
						_logger.LogTrace(responseMessage);
					}
					else if (response.ResponseCode != 429) // HttpStatusCode.TooManyRequests
					{
						Disconnect();
					}
				}
			}
			catch (HttpRequestException httpException)
			{
				_logger.LogError(httpException, httpException.ToString());
				InternalDisconnect();
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Request failed {0}", e.ToString());
			}

			try
			{
				if (!string.IsNullOrEmpty(responseMessage))
				{
					ProcessMessage(responseMessage);
				}
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_sendSemaphore.Release();
				}
			}
		}

		private void DoSendBatch(IList<IDictionary<string, object>> messages)
		{
			var batch = new Dictionary<string, object>()
			{
				["t"] = "server.batch",
				["id"] = -100,
				["data"] = new Dictionary<string, object>()
				{
					["list"] = messages,
				}
			};

			DoSendRequest(batch);
		}

		#region Heartbeat

		private void UpdateHeartbeat()
		{
			// Determine if heartbeat should be running
			bool shouldRun = _connected && GetCurrentHeartbeatInterval().TotalSeconds >= 1;

			if (shouldRun && !_heartbeatRunning)
			{
				StartHeartbeat();
			}
			else if (!shouldRun && _heartbeatRunning)
			{
				StopHeartbeat();
			}
			// If already running, but interval changed (e.g., mode switched), restart
			else if (shouldRun && _heartbeatRunning)
			{
				StopHeartbeat();
				StartHeartbeat();
			}
		}

		private TimeSpan GetCurrentHeartbeatInterval()
		{
			return IntensiveHeartbeat ? _heartbeatIntensiveInterval : _config.HttpHeartbeatInterval;
		}

		private void StartHeartbeat()
		{
			StopHeartbeat(); // Ensure only one task is running

			var interval = GetCurrentHeartbeatInterval();
			if (interval.TotalSeconds < 1)
			{
				return;
			}

			_heartbeatRunning = true;
			_heartbeatCancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => HeartbeatTask(_heartbeatCancellation.Token));
		}

		private void StopHeartbeat()
		{
			if (_heartbeatCancellation != null)
			{
				_heartbeatCancellation.Cancel();
				_heartbeatCancellation = null;
			}
			_heartbeatRunning = false;
		}

		private async void HeartbeatTask(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested && Connected)
			{
				var interval = GetCurrentHeartbeatInterval();
				if (interval.TotalSeconds < 1)
					break;

				try
				{
					await AlterTask.Delay(interval, cancellation);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (!cancellation.IsCancellationRequested && Connected)
				{
					SendMessage(s_pingMessage);
				}
			}
			_heartbeatRunning = false;
		}

		#endregion

		private async void SendHandshake()
		{
			bool semaphoreOccupied = false;

			try
			{
				await _sendSemaphore.WaitAsync();
				semaphoreOccupied = true;

				string url = _config.GetHttpAddress();
				var uri = new Uri(new Uri(url), "test_connect.html");

				_logger.LogTrace($"<<< request ({uri})");

				using (var response = await _client.Get(uri))
				{
					_logger.LogTrace($">>> response {uri} ({response.ResponseCode}) {response.Error}");

					if (response.IsSuccess)
					{
						ConfirmConnectionEstablished();
					}
				}
			}
			catch (HttpRequestException httpException)
			{
				if (_connectionEstablished)
				{
					_logger.LogError(httpException, httpException.ToString());
				}
				else
				{
					_logger.LogTrace("SendHandshake error: " + httpException);
				}
			}
			catch (Exception e)
			{
				if (_connectionEstablished)
				{
					_logger.LogError(e, "Request failed {0}", e.ToString());
				}
				else
				{
					_logger.LogTrace("SendHandshake error: " + e);
				}
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_sendSemaphore.Release();
				}
			}

			if (!_connected)
			{
				InternalDisconnect();
			}
		}

		private void ConfirmConnectionEstablished()
		{
			_connected = true;
			_connectionEstablished = true;
			OnClientConnected();

			UpdateHeartbeat();
		}


		public override void Dispose()
		{
			if (_client is IDisposable disposableClient)
			{
				disposableClient.Dispose();
			}

			base.Dispose();
		}
	}
}
