using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MiniIT.Snipe;
using UnityEngine;
using System.Linq;

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

		private SnipeContext _snipeContext;
		private bool _running = false;

		private HttpClient _httpClient;

		private static readonly List<LogRecord> _log = new List<LogRecord>();
		private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

		static LogReporter()
		{
			Application.logMessageReceivedThreaded += OnLogMessageReceived;
		}

		public LogReporter(SnipeContext snipeContext)
		{
			_snipeContext = snipeContext;
		}

		public async Task<bool> SendAsync()
		{
			string apiKey = _snipeContext.Config.ClientKey;
			string url = _snipeContext.Config.LogReporterUrl;

			if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(url))
			{
				DebugLogWarning($"[{nameof(LogReporter)}] Invalid apiKey or url");
				return false;
			}
			
			try
			{
				await _semaphore.WaitAsync();
				
				if (_running)
				{
					DebugLogWarning($"[{nameof(LogReporter)}] Already running");
					return false;
				}
				_running = true;
			}
			finally
			{
				_semaphore.Release();
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
				try
				{
					await _semaphore.WaitAsync();
					content = await Task.Run(() => GetPortionContent(ref startIndex, connectionId, userId, appVersion, appPlatform));
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
					_semaphore.Release();
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

				try
				{
					await _semaphore.WaitAsync();
					
					_log.Clear();
				}
				finally
				{
					_running = false;
					_semaphore.Release();
				}
			}

			return succeeded;
		}

		private string GetPortionContent(ref int startIndex, int connectionId, int userId, string version, RuntimePlatform platform)
		{
#if ZSTRING
			ursing var content = ZString.CreateStringBuilder(true);
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
			try
			{
				await _semaphore.WaitAsync();
				
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
				_semaphore.Release();
			}
		}

		// https://stackoverflow.com/a/14087738
		private static string Escape(string input)
		{
			StringBuilder literal = new StringBuilder(input.Length + 2);
			foreach (var c in input)
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

		private void DebugLog(string text)
		{
			var stackType = Application.GetStackTraceLogType(LogType.Log);
			Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
			Debug.Log(text);
			Application.SetStackTraceLogType(LogType.Log, stackType);
		}

		private void DebugLogWarning(string text)
		{
			var stackType = Application.GetStackTraceLogType(LogType.Warning);
			Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
			Debug.LogWarning(text);
			Application.SetStackTraceLogType(LogType.Warning, stackType);
		}

		private void DebugLogError(string text)
		{
			var stackType = Application.GetStackTraceLogType(LogType.Error);
			Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
			Debug.LogError(text);
			Application.SetStackTraceLogType(LogType.Error, stackType);
		}

		#endregion
	}
}
