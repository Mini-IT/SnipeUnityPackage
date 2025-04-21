using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Threading;

namespace MiniIT.Snipe
{
	public class HttpTransport : Transport
	{
		private const string API_PATH = "api/v1/request/";

		private static readonly SnipeObject s_pingMessage = new SnipeObject() { ["t"] = "server.ping", ["id"] = -1 };
		private static readonly long s_sessionDurationTicks = TimeSpan.FromSeconds(301).Ticks;

		public override bool Started => _started;
		public override bool Connected => _connected;
		public override bool ConnectionEstablished => _connectionEstablished;

		private IHttpClient _client;

		private TimeSpan _heartbeatInterval;
		private long _sessionAliveTillTicks;

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
			_sessionAliveTillTicks = 0;

			StopHeartbeat();

			_client?.Reset();

			ConnectionClosedHandler?.Invoke(this);

			// It's important to keep the value during ConnectionClosedHandler invocation
			_connectionEstablished = false;
		}

		public override void SendMessage(SnipeObject message)
		{
			if (!CheckSessionAlive())
			{
				Disconnect();
				return;
			}

			DoSendRequest(message);
		}

		public override void SendBatch(IList<SnipeObject> messages)
		{
			if (!CheckSessionAlive())
			{
				Disconnect();
				return;
			}

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

		private void ProcessMessage(string json)
		{
			var message = SnipeObject.FromJSONString(json);

			if (message == null)
			{
				return;
			}

			RefreshSessionAliveTimestamp();

			if (message.SafeGetString("t") == "server.responses")
			{
				if (!message.TryGetValue("data", out var innerData))
				{
					// Wrong message format
					return;
				}

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
			else // single message
			{
				InternalProcessMessage(message);
			}
		}

		private void ProcessBatchInnerMessages(IList innerMessages)
		{
			foreach (var innerMessage in innerMessages)
			{
				if (innerMessage is SnipeObject message)
				{
					InternalProcessMessage(message);
				}
			}
		}

		private void InternalProcessMessage(SnipeObject message)
		{
			_logger.LogTrace(message.ToJSONString());

			if (message.SafeGetString("t") == "user.login")
			{
				ExtractAuthToken(message);
			}

			MessageReceivedHandler?.Invoke(message);
		}

		private void ExtractAuthToken(SnipeObject message)
		{
			if (!message.TryGetValue("token", out string token))
			{
				if (message.TryGetValue<SnipeObject>("data", out var data))
				{
					data.TryGetValue("token", out token);
				}
			}

			if (!string.IsNullOrEmpty(token))
			{
				_client.SetAuthToken(token);
			}
		}

		private async void DoSendRequest(SnipeObject message)
		{
			string requestType = message.SafeGetString("t");
			int requestId = message.SafeGetValue<int>("id");
			string json = message.ToJSONString(); // Don't use FastJSON because the message can contain custom classes for attr.setMulty for example

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
				string exceptionMessage = (e is AggregateException) ? LogUtil.GetReducedException(e) : e.ToString();
				_logger.LogError(e, "Request failed {0}", exceptionMessage);
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

		private void DoSendBatch(IList<SnipeObject> messages)
		{
			var batch = new SnipeObject()
			{
				["t"] = "server.batch",
				["id"] = -100,
				["data"] = new SnipeObject()
				{
					["list"] = messages,
				}
			};

			DoSendRequest(batch);
		}

		#region Heartbeat

		private CancellationTokenSource _heartbeatCancellation;

		private void StartHeartbeat()
		{
			_heartbeatCancellation?.Cancel();

			if (_config.HttpHeartbeatInterval.TotalSeconds < 1)
			{
				return;
			}

			_heartbeatInterval = _config.HttpHeartbeatInterval;

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
		}

		private async void HeartbeatTask(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested && Connected)
			{
				try
				{
					await AlterTask.Delay(_heartbeatInterval, cancellation);
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
				string exceptionMessage = (e is AggregateException) ? LogUtil.GetReducedException(e) : e.ToString();

				if (_connectionEstablished)
				{
					_logger.LogError(e, "Request failed {0}", exceptionMessage);
				}
				else
				{
					_logger.LogTrace("SendHandshake error: " + exceptionMessage);
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
			RefreshSessionAliveTimestamp();

			_logger.LogTrace("OnClientConnected");

			ConnectionOpenedHandler?.Invoke(this);

			StartHeartbeat();
		}

		private void RefreshSessionAliveTimestamp()
		{
			_sessionAliveTillTicks = Stopwatch.GetTimestamp() + s_sessionDurationTicks;
		}

		private bool CheckSessionAlive()
		{
			return Stopwatch.GetTimestamp() < _sessionAliveTillTicks;
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
