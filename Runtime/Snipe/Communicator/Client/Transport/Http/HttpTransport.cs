using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe
{
	public class HttpTransport : Transport
	{
		private const string API_PATH = "api/v1/request/";

		public override bool Started => _connected;
		public override bool Connected => _connected;

		private HttpClient _httpClient;
		private CancellationTokenSource _networkLoopCancellation;

		private Uri _baseUrl;
		private bool _connected;
		private readonly object _lock = new object();

		internal HttpTransport(SnipeConfig config, Analytics analytics)
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
			OnClientConnected();

			//StartNetworkLoop();
		}

		public override void Disconnect()
		{
			_connected = false;

			if (_httpClient != null)
			{
				_httpClient.DefaultRequestHeaders.Authorization = null;
			}
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
			_logger.Log("OnClientConnected");

			ConnectionOpenedHandler?.Invoke();
		}

		//private void OnClientDisconnected()
		//{
		//	_logger.Log($"OnClientDisconnected");

		//	//StopNetworkLoop();
		//	Connected = false;

		//	//if (!ConnectionEstablished) // failed to establish connection
		//	//{
		//	//	if (_config.NextUdpUrl())
		//	//	{
		//	//		_logger.Log($"Next udp url");
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

				_logger.Log($"+++ <<< request ({uri}) - {json}");

				using (var response = await _httpClient.PostAsync(uri, requestContent))
				{
					// response.StatusCode:
					//   200 - ok
					//   401 - wrong auth token,
					//   429 - {"errorCode":"rateLimit"}
					//   500 - {"errorCode":"requestTimeout"}

					string responseMessage = await response.Content.ReadAsStringAsync();

					_logger.Log($"+++ >>> response {requestType} ({(int)response.StatusCode} {response.StatusCode}) {responseMessage}");

					if (response.IsSuccessStatusCode)
					{
						ProcessMessage(responseMessage);
					}
					else if (response.StatusCode == HttpStatusCode.Unauthorized) // 401 - wrong auth token
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
				["list"] = messages,
			};

			DoSendRequest(batch);
		}

		

		/*

		private void StartNetworkLoop()
		{
			_logger.Log($"StartNetworkLoop");
			
			_networkLoopCancellation?.Cancel();

			_networkLoopCancellation = new CancellationTokenSource();
			Task.Run(() => NetworkLoop(_networkLoopCancellation.Token));
			//Task.Run(() => UdpConnectionTimeout(_networkLoopCancellation.Token));
		}

		public void StopNetworkLoop()
		{
			_logger.Log($"StopNetworkLoop");
			
			if (_networkLoopCancellation != null)
			{
				_networkLoopCancellation.Cancel();
				_networkLoopCancellation = null;
			}
		}

		private async void NetworkLoop(CancellationToken cancellation)
		{
			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				try
				{
					_kcpConnection?.Tick();
					//_analytics.PingTime = _kcpConnection?.connection?.PingTime ?? 0;
				}
				catch (Exception e)
				{
					_logger.Log($"NetworkLoop - Exception: {e}");
					_analytics.TrackError("NetworkLoop error", e);
					OnClientDisconnected();
					return;
				}
				
				try
				{
					await Task.Delay(100, cancellation);
				}
				catch (TaskCanceledException)
				{
					// This is OK. Just terminating the task
					return;
				}
			}
		}

		*/
		
		//private async void UdpConnectionTimeout(CancellationToken cancellation)
		//{
		//	_logger.Log($"UdpConnectionTimeoutTask - start");
			
		//	try
		//	{
		//		await Task.Delay(2000, cancellation);
		//	}
		//	catch (TaskCanceledException)
		//	{
		//		// This is OK. Just terminating the task
		//		return;
		//	}
			
		//	if (cancellation == null || cancellation.IsCancellationRequested)
		//		return;
		//	if (cancellation != _networkLoopCancellation?.Token)
		//		return;
			
		//	if (!Connected)
		//	{
		//		_logger.Log($"UdpConnectionTimeoutTask - Calling Disconnect");
		//		OnClientDisconnected();
		//	}
			
		//	_logger.Log($"UdpConnectionTimeoutTask - finish");
		//}
	}
}
