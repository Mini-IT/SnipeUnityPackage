using System;
using System.Text;
using System.Net;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.IO;
using MiniIT.Http;
using MiniIT.Snipe.Logging;

#if ZSTRING
using Cysharp.Text;
#endif

namespace MiniIT.Snipe.Internal
{
	internal class LogSender
	{
		private const int MAX_CHUNK_LENGTH = 200 * 1024;
		private const int MAX_SEND_ATTEMPTS = 3;
		private const int INITIAL_RETRY_DELAY_MS = 500;
		private const long MIN_SUCCESS_HTTP_CODE = 200;
		private const long MAX_SUCCESS_HTTP_CODE_EXCLUSIVE = 300;

		private static readonly TimeSpan _sendTimeout = TimeSpan.FromSeconds(5);

		private readonly SnipeContext _snipeContext;
		private readonly string _apiKey;
		private readonly string _url;
		private readonly ISnipeServices _services;

		public LogSender(SnipeContext snipeContext, SnipeOptions snipeOptions, ISnipeServices services)
		{
			_snipeContext = snipeContext;
			_services = services ?? throw new ArgumentNullException(nameof(services));

			_apiKey = snipeOptions.ClientKey;
			_url = snipeOptions.LogReporterUrl;
		}

		internal async UniTask<bool> SendAsync(StreamReader file)
		{
			if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_url))
			{
				DebugLogger.LogWarning($"[{nameof(LogSender)}] Invalid apiKey or url");
				return false;
			}

			int connectionId = 0;
			int userId = 0;
			if (_snipeContext?.Communicator != null)
			{
				int.TryParse(_snipeContext.Communicator.ConnectionId, out connectionId);
				userId = _snipeContext.Auth?.UserID ?? 0;
			}
			string appVersion = Application.version;
			RuntimePlatform appPlatform = Application.platform;

			bool succeeded = true;
			HttpStatusCode statusCode = default;

			string line = null;

			IHttpClient httpClient = _services.HttpClientFactory.CreateHttpClient();
			httpClient.SetAuthToken(_apiKey);
			var uri = new Uri(_url);

			while (!file.EndOfStream)
			{
				string content = GetPortionContent(file, ref line, connectionId, userId, appVersion, appPlatform);

				IHttpClientResponse response = null;

				try
				{
					response = await PostWithRetryAsync(httpClient, uri, content);
					statusCode = (HttpStatusCode)(response?.ResponseCode ?? 0);

					if (!IsSuccessfulHttpResponse(response))
					{
						succeeded = false;
						DebugLogger.Log($"[{nameof(LogSender)}] Failed posting log. Result code = {(int)statusCode} {statusCode} {response?.Error ?? "No response"}");
						break;
					}

					DebugLogger.Log($"[{nameof(LogSender)}] Send log portion result code = {(int)statusCode} {statusCode}");
				}
				catch (Exception ex)
				{
					succeeded = false;
					DebugLogger.LogError($"[{nameof(LogSender)}] Error posting log portion: {LogUtil.GetReducedException(ex)}");
					break;
				}
				finally
				{
					response?.Dispose();
				}
			}

			if (succeeded)
			{
				DebugLogger.Log($"[{nameof(LogSender)}] Sent successfully. UserId = {userId}, ConnectionId = {connectionId}");
			}

			if (httpClient is IDisposable disposable)
			{
				disposable.Dispose();
			}

			return succeeded;
		}

		private async UniTask<IHttpClientResponse> PostWithRetryAsync(IHttpClient httpClient, Uri uri, string content)
		{
			for (int attempt = 1; attempt <= MAX_SEND_ATTEMPTS; attempt++)
			{
				IHttpClientResponse response = null;

				try
				{
					response = await httpClient.PostJson(uri, content, _sendTimeout);
				}
				catch (Exception) when (attempt < MAX_SEND_ATTEMPTS)
				{
				}

				if (IsSuccessfulHttpResponse(response) || !IsRetryableTransportFailure(response) || attempt == MAX_SEND_ATTEMPTS)
				{
					return response;
				}

				response?.Dispose();

				int delayMs = INITIAL_RETRY_DELAY_MS * (1 << (attempt - 1));
				await UniTask.Delay(delayMs, ignoreTimeScale: true);
			}

			return null;
		}

		private bool IsSuccessfulHttpResponse(IHttpClientResponse response)
		{
			return response != null && response.IsSuccess &&
				response.ResponseCode >= MIN_SUCCESS_HTTP_CODE && response.ResponseCode < MAX_SUCCESS_HTTP_CODE_EXCLUSIVE;
		}

		private bool IsRetryableTransportFailure(IHttpClientResponse response)
		{
			return response == null || response.ResponseCode <= 0 ||
				(!response.IsSuccess && response.ResponseCode == (long)HttpStatusCode.RequestTimeout);
		}

		private string GetPortionContent(StreamReader file, ref string line, int connectionId, int userId, string appVersion, RuntimePlatform appPlatform)
		{
#if ZSTRING
			using var contentBuilder = ZString.CreateStringBuilder(true);
#else
			var contentBuilder = new StringBuilder();
#endif
			contentBuilder.Append("{");
			contentBuilder.Append($"\"connectionID\":{connectionId},");
			contentBuilder.Append($"\"userID\":{userId},");
			contentBuilder.Append($"\"version\":\"{appVersion}\",");
			contentBuilder.Append($"\"platform\":\"{appPlatform}\",");
			contentBuilder.Append("\"list\":[");

			bool linesAdded = false;

			if (!string.IsNullOrEmpty(line))
			{
				contentBuilder.Append(line);
				line = null;
				linesAdded = true;
			}

			while (!file.EndOfStream)
			{
				line = file.ReadLine(); // ReadLineAsync?

				if (linesAdded && contentBuilder.Length + line.Length > MAX_CHUNK_LENGTH)
				{
					break;
				}

				if (linesAdded)
				{
					contentBuilder.Append(",");
				}

				contentBuilder.Append(line);

				linesAdded = true;
			}

			contentBuilder.Append("]}");

			return contentBuilder.ToString();
		}
	}
}
