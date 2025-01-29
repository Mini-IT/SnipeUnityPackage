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

		public override bool Started => _connected;
		public override bool Connected => _connected;
		public override bool ConnectionEstablished => _connectionEstablished;

		private IHttpClient _client;

		private TimeSpan _heartbeatInterval;

		private Uri _baseUrl;
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
				if (Connected)
					return;

				_baseUrl = GetBaseUrl();

				_client ??= HttpClientFactory.Create();
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

			_client?.Reset();

			StopHeartbeat();
			ConnectionClosedHandler?.Invoke(this);

			// It's important to keep the value during ConnectionClosedHandler invocation
			_connectionEstablished = false;
		}

		public override void SendMessage(SnipeObject message)
		{
			DoSendRequest(message);
		}

		public override void SendBatch(List<SnipeObject> messages)
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
			var message = SnipeObject.FromJSONString(json);

			if (message != null)
			{
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
			ExtractAuthToken(message);
			MessageReceivedHandler?.Invoke(message);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ExtractAuthToken(SnipeObject message)
		{
			_logger.LogTrace(message.ToJSONString());

			if (message.SafeGetString("t") == "user.login")
			{
				string token = null;

				if (!message.TryGetValue("token", out token))
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
		}

		private async void DoSendRequest(SnipeObject message)
		{
			string requestType = message.SafeGetString("t");
			string json = message.ToJSONString(); // Don't use FastJSON because the message can contain custom classes for attr.setMulty for example

			string responseMessage = null;

			bool semaphoreOccupied = false;

			try
			{
				await _sendSemaphore.WaitAsync();
				semaphoreOccupied = true;

				var uri = new Uri(_baseUrl, requestType);

				_logger.LogTrace($"<<< request ({uri}) - {requestType}");

				using (var response = await _client.PostJsonAsync(uri, json))
				{
					// response.StatusCode:
					//   200 - ok
					//   401 - wrong auth token,
					//   429 - {"errorCode":"rateLimit"}
					//   500 - {"errorCode":"requestTimeout"}

					_logger.LogTrace($">>> response {requestType} ({response.ResponseCode}) {response.Error}");

					if (response.IsSuccess)
					{
						responseMessage = await response.GetContentAsync();
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

		private void DoSendBatch(List<SnipeObject> messages)
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
			int milliseconds = (int)_heartbeatInterval.TotalMilliseconds;
			var message = new SnipeObject()
			{
				["t"] = "server.ping",
				["id"] = -1,
			};

			while (cancellation != null && !cancellation.IsCancellationRequested && Connected)
			{
				try
				{
					await AlterTask.Delay(milliseconds, cancellation);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (!cancellation.IsCancellationRequested && Connected)
				{
					SendMessage(message);
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

				using (var response = await _client.GetAsync(uri))
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
				_logger.LogError(httpException, httpException.ToString());
				InternalDisconnect();
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Request failed {0}", e.ToString());
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_sendSemaphore.Release();
				}
			}
		}

		private void ConfirmConnectionEstablished()
		{
			_connected = true;
			_connectionEstablished = true;
			OnClientConnected();

			StartHeartbeat();
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
