using System;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MiniIT.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.IO;

#if ZSTRING
using Cysharp.Text;
#endif

namespace MiniIT.Snipe.Internal
{
	internal class LogSender : IDisposable
	{
		private const int MAX_CHUNK_LENGTH = 200 * 1024;

		private SnipeContext _snipeContext;
		private HttpClient _httpClient;
		private bool _running = false;

		private readonly AlterSemaphore _semaphore;

		public LogSender(AlterSemaphore semaphore)
		{
			_semaphore = semaphore;
		}

		internal void SetSnipeContext(SnipeContext snipeContext)
		{
			_snipeContext = snipeContext;
		}

		public void Dispose()
		{
			if (_httpClient != null)
			{
				try
				{
					_httpClient.Dispose();
				}
				catch (Exception) { }

				_httpClient = null;
			}
		}

		internal async UniTask<bool> SendAsync(StreamReader file)
		{
			string apiKey = _snipeContext.Config.ClientKey;
			string url = _snipeContext.Config.LogReporterUrl;

			if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(url))
			{
				DebugLogger.LogWarning($"[{nameof(LogSender)}] Invalid apiKey or url");
				return false;
			}

			bool semaphoreOccupied = false;

			try
			{
				await _semaphore.WaitAsync();
				semaphoreOccupied = true;

				if (_running)
				{
					DebugLogger.LogWarning($"[{nameof(LogSender)}] Already running");
					return false;
				}
				_running = true;
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_semaphore.Release();
				}
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

			_httpClient ??= new HttpClient();
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

			string line = null;

			while (!file.EndOfStream)
			{
				semaphoreOccupied = false;

				try
				{
					await _semaphore.WaitAsync();
					semaphoreOccupied = true;

					string content = GetPortionContent(file, ref line, connectionId, userId, appVersion, appPlatform);
					var requestContent = new StringContent(content, Encoding.UTF8, "application/json");

					try
					{
						HttpResponseMessage result = await _httpClient.PostAsync(url, requestContent);

						statusCode = result.StatusCode;

						if (!result.IsSuccessStatusCode)
						{
							succeeded = false;
							break;
						}
					}
					catch (Exception ex)
					{
						succeeded = false;
						statusCode = HttpStatusCode.BadRequest;
						DebugLogger.LogError($"[{nameof(LogSender)}] - Error posting log portion: {ex}");
						break;
					}
				}
				catch (Exception ex)
				{
					succeeded = false;
					statusCode = HttpStatusCode.BadRequest;
					DebugLogger.LogError($"[{nameof(LogSender)}] - Error getting log portion: {ex}");
					break;
				}
				finally
				{
					if (semaphoreOccupied)
					{
						_semaphore.Release();
					}
				}

				DebugLogger.Log($"[{nameof(LogSender)}] - Send log portion result code = {(int)statusCode} {statusCode}");

				if (!succeeded)
				{
					break;
				}
			}

			if (succeeded)
			{
				DebugLogger.Log($"[{nameof(LogSender)}] - Sent successfully. UserId = {userId}, ConnectionId = {connectionId}");
			}

			_running = false;

			return succeeded;
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
