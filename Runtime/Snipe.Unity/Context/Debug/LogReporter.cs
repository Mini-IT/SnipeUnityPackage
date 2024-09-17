using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MiniIT.Snipe;
using MiniIT.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

#if ZSTRING
using Cysharp.Text;
#endif

namespace MiniIT
{
	public class LogReporter : IDisposable
	{
		internal class LogRecord
		{
			internal long _time;
			internal string _type;
			internal string _message;
			internal string _stackTrace;
		}

		private const int MAX_CHUNK_LENGTH = 200 * 1024;

		private readonly SnipeContext _snipeContext;
		private bool _running = false;

		private HttpClient _httpClient;

		private static readonly List<LogRecord> _log = new List<LogRecord>();
		private static readonly AlterSemaphore _semaphore = new AlterSemaphore(1, 1);

		static LogReporter()
		{
			Application.logMessageReceivedThreaded += OnLogMessageReceived;
		}

		public LogReporter(SnipeContext snipeContext)
		{
			_snipeContext = snipeContext;
		}

		public async UniTask<bool> SendAsync()
		{
			string apiKey = _snipeContext.Config.ClientKey;
			string url = _snipeContext.Config.LogReporterUrl;

			if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(url))
			{
				DebugLogWarning($"[{nameof(LogReporter)}] Invalid apiKey or url");
				return false;
			}

			bool semaphoreOccupied = false;

			try
			{
				await _semaphore.WaitAsync();
				semaphoreOccupied = true;

				if (_running)
				{
					DebugLogWarning($"[{nameof(LogReporter)}] Already running");
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

			for (int startIndex = 0; startIndex < _log.Count;)
			{
				string content;
				semaphoreOccupied = false;

				try
				{
					await _semaphore.WaitAsync();
					semaphoreOccupied = true;

					content = await AlterTask.Run(() => GetPortionContent(ref startIndex, connectionId, userId, appVersion, appPlatform));
				}
				catch (Exception ex)
				{
					succeeded = false;
					statusCode = HttpStatusCode.BadRequest;
					DebugLogError($"[{nameof(LogReporter)}] - Error getting log portion: {ex}");
					break;
				}
				finally
				{
					if (semaphoreOccupied)
					{
						_semaphore.Release();
					}
				}
					
				var requestContent = new StringContent(content, Encoding.UTF8, "application/json");

				try
				{
					var result = await _httpClient.PostAsync(url, requestContent);

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
					DebugLogError($"[{nameof(LogReporter)}] - Error posting log portion: {ex}");
					break;
				}
			}

			DebugLog($"[{nameof(LogReporter)}] - Send result code = {(int)statusCode} {statusCode}");

			if (succeeded)
			{
				DebugLog($"[{nameof(LogReporter)}] - Sent successfully. UserId = {userId}, ConnectionId = {connectionId}");

				semaphoreOccupied = false;

				try
				{
					await _semaphore.WaitAsync();
					semaphoreOccupied = true;

					_log.Clear();
				}
				finally
				{
					_running = false;

					if (semaphoreOccupied)
					{
						_semaphore.Release();
					}
				}
			}

			return succeeded;
		}

		private string GetPortionContent(ref int startIndex, int connectionId, int userId, string version, RuntimePlatform platform)
		{
#if ZSTRING
			using var content = ZString.CreateStringBuilder(true);
			content.Append("{");
#else
			var content = new StringBuilder("{");
#endif
			content.Append($"\"connectionID\":{connectionId},");
			content.Append($"\"userID\":{userId},");
			content.Append($"\"version\":\"{version}\",");
			content.Append($"\"platform\":\"{platform}\",");
			content.Append("\"list\":[");

			bool linesAdded = false;
			while (startIndex < _log.Count)
			{
				var item = _log[startIndex];
				
				string line = $"{{\"time\":{item._time},\"level\":\"{item._type}\",\"msg\":\"{Escape(item._message)}\",\"stack\":\"{Escape(item._stackTrace)}\"}}";
				if (linesAdded && content.Length + line.Length > MAX_CHUNK_LENGTH)
				{
					break;
				}

				if (linesAdded)
				{
					content.Append(",");
				}
				content.Append(line);

				startIndex++;
				linesAdded = true;
			}

			content.Append("]}");
			return content.ToString();
		}

		private static async void OnLogMessageReceived(string condition, string stackTrace, LogType type)
		{
			bool semaphoreOccupied = false;

			try
			{
				await _semaphore.WaitAsync();
				semaphoreOccupied = true;

				_log.Add(new LogRecord()
				{
					_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
					_type = type.ToString(),
					_message = condition,
					_stackTrace = stackTrace,
				});
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_semaphore.Release();
				}
			}
		}

		// https://stackoverflow.com/a/14087738
		private static string Escape(string input)
		{
			StringBuilder literal = new StringBuilder(input.Length + 2);
			foreach (char c in input)
			{
				switch (c)
				{
					case '\"': literal.Append("\\\""); break;
					case '\\': literal.Append(@"\\"); break;
					case '\0': literal.Append(@"\0"); break;
					case '\a': literal.Append(@"\a"); break;
					case '\b': literal.Append(@"\b"); break;
					case '\f': literal.Append(@"\f"); break;
					case '\n': literal.Append(@"\n"); break;
					case '\r': literal.Append(@"\r"); break;
					case '\t': literal.Append(@"\t"); break;
					case '\v': literal.Append(@"\v"); break;
					default:
						// ASCII printable character
						if (c >= 0x20 && c <= 0x7e)
						{
							literal.Append(c); // As UTF16 escaped character
						}
						else
						{
							literal.Append(@"\u");
							literal.Append(((int)c).ToString("x4"));
						}
						break;
				}
			}
			return literal.ToString();
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

		#region DebugLog

		private void DebugLog(LogType logType, string text)
		{
			var stackType = Application.GetStackTraceLogType(logType);
			Application.SetStackTraceLogType(logType, StackTraceLogType.ScriptOnly);
			Debug.Log(text);
			Application.SetStackTraceLogType(logType, stackType);
		}

		private void DebugLog(string text) => DebugLog(LogType.Log, text);
		private void DebugLogWarning(string text) => DebugLog(LogType.Warning, text);
		private void DebugLogError(string text) => DebugLog(LogType.Error, text);

		#endregion
	}
}
