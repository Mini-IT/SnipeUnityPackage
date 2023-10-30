using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public class HttpTransport : Transport
	{
		private const string API_PATH = "api/v1/request/";

		public override bool Started => _connected;
		public override bool Connected => _connected;
		public override bool ConnectionEstablished => _connectionEstablished;

		private HttpClient _httpClient;

		private TimeSpan _heartbeatInterval;

		private Uri _baseUrl;
		private bool _connected;
		private bool _connectionEstablished = false;
		private readonly object _lock = new object();

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

				_httpClient ??= new HttpClient();
			}

			_connected = true;
			_connectionEstablished = true;
			OnClientConnected();

			StartHeartbeat();
		}

		public override void Disconnect()
		{
			if (!_connected)
			{
				return;
			}

			_connected = false;

			if (_httpClient != null)
			{
				_httpClient.DefaultRequestHeaders.Clear();
			}

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

		//private void OnClientDisconnected()
		//{
		//	_logger.LogTrace($"OnClientDisconnected");

		//	//StopNetworkLoop();
		//	Connected = false;

		//	//if (!ConnectionEstablished) // failed to establish connection
		//	//{
		//	//	if (_config.NextUdpUrl())
		//	//	{
		//	//		_logger.LogTrace($"Next udp url");
		//	//	}
		//	//}

		//	ConnectionClosedHandler?.Invoke();
		//}

		private void ProcessMessage(string json)
		{
			var message = SnipeObject.FromJSONString(json);

			if (message != null)
			{
				ExtractAuthToken(message);

				if (message.SafeGetString("t") == "server.responses")
				{
					if (message.TryGetValue("data", out IList<SnipeObject> innerMessages))
					{
						foreach (var innerMessage in innerMessages)
						{
							MessageReceivedHandler?.Invoke(innerMessage);
						}
					}
				}
				else // single message
				{
					MessageReceivedHandler?.Invoke(message);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ExtractAuthToken(SnipeObject message)
		{
			if (message.TryGetValue("token", out string token) && message.SafeGetString("t") == "user.login")
			{
				_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
			}
		}

		private async void DoSendRequest(SnipeObject message)
		{
			string requestType = message.SafeGetString("t");
			string json = message.ToFastJSONString();

			try
			{
				var uri = new Uri(_baseUrl, requestType);
				var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

				_logger.LogTrace($"<<< request ({uri}) - {json}");

				using (var response = await _httpClient.PostAsync(uri, requestContent))
				{
					// response.StatusCode:
					//   200 - ok
					//   401 - wrong auth token,
					//   429 - {"errorCode":"rateLimit"}
					//   500 - {"errorCode":"requestTimeout"}

					string responseMessage = await response.Content.ReadAsStringAsync();

					_logger.LogTrace($">>> response {requestType} ({(int)response.StatusCode} {response.StatusCode}) {responseMessage}");
					
					if (response.IsSuccessStatusCode)
					{
						ProcessMessage(responseMessage);
					}
					else
#if NET_STANDARD_2_1
					if (response.StatusCode != HttpStatusCode.TooManyRequests)
#else
					if ((int)response.StatusCode != 429)
#endif
					{
						Disconnect();
					}
				}
			}
			catch (HttpRequestException e)
			{
				_logger.LogError(e, $"{e}");
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
			Task.Run(() => HeartbeatTask(_heartbeatCancellation.Token));
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
					await Task.Delay(milliseconds, cancellation);
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
	}
}
