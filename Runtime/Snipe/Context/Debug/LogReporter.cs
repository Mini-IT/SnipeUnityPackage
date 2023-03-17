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
#if ZSTRING
using Cysharp.Text;
#endif

namespace MiniIT
{
	public class LogReporter : MonoBehaviour
	{
		private static LogReporter _instance;

		internal class LogRecord
		{
			internal long _time;
			internal string _type;
			internal string _message;
			internal string _stackTrace;
		}

		private const int PORTION_SIZE = 200; // messages

		private readonly List<LogRecord> _log = new List<LogRecord>();
		private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

		public static void InitInstance()
		{
			if (_instance != null)
				return;

			_instance = FindObjectOfType<LogReporter>();

			if (_instance == null)
			{
				_instance = new GameObject("[LogReporter]").AddComponent<LogReporter>();
			}
		}

		private void Awake()
		{
			if (_instance != null && _instance != this)
			{
				DestroyImmediate(this.gameObject);
				return;
			}
			_instance = this;
			DontDestroyOnLoad(this.gameObject);

			Application.logMessageReceivedThreaded += OnLogMessageReceived;
		}

		private void OnDestroy()
		{
			Application.logMessageReceivedThreaded -= OnLogMessageReceived;
		}

		public static async Task<bool> SendAsync(SnipeContext snipeContext = null)
		{
			if (_instance == null)
			{
				Debug.LogWarning("[LogReporter] Instance not initialized");
				return false;
			}

			return await _instance.DoSendAsync(SnipeConfig.ClientKey, SnipeConfig.LogReporterUrl, snipeContext);
		}

		private async Task<bool> DoSendAsync(string apiKey, string url, SnipeContext snipeContext)
		{
			if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(url))
			{
				Debug.LogWarning("[LogReporter] Invalid apiKey or url");
				return false;
			}

			int connectionId = 0;
			int userId = 0;
			if (snipeContext?.Communicator != null)
			{
				int.TryParse(snipeContext.Communicator.ConnectionId, out connectionId);
				userId = snipeContext.Auth?.UserID ?? 0;
			}

			bool succeeded = true;
			HttpStatusCode statusCode = default;

			using (var httpClient = new HttpClient())
			{
				httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

				for (int i = 0; i < _log.Count; i += PORTION_SIZE)
				{
					string content = await GetPortionContent(i, connectionId, userId);
					
					var requestContent = new StringContent(content, Encoding.UTF8, "application/json");
					var result = await httpClient.PostAsync(url, requestContent);

					statusCode = result.StatusCode;
					
					if (!result.IsSuccessStatusCode)
					{
						succeeded = false;
						break;
					}
				}
			}

			Debug.Log($"[LogReporter] - Send result code = {(int)statusCode} {statusCode}");

			if (succeeded)
			{
				Debug.Log($"[LogReporter] - Sent successfully. UserId = {userId}, ConnectionId = {connectionId}");

				try
				{
					await _semaphore.WaitAsync();
					
					_log.Clear();
				}
				finally
				{
					_semaphore.Release();
				}
			}

			return succeeded;
		}

		private async Task<string> GetPortionContent(int startIndex, int connectionId, int userId)
		{
#if ZSTRING
			var content = ZString.CreateStringBuilder(true);
			content.Append("{");
#else
			var content = new StringBuilder("{");
#endif
			content.Append($"\"connectionID\":{connectionId},");
			content.Append($"\"userID\":{userId},");
			content.Append($"\"version\":\"{Application.version}\",");
			content.Append($"\"platform\":\"{Application.platform}\",");
			content.Append("\"list\":[");

			for (int i = 0; i < PORTION_SIZE; i++)
			{
				int index = startIndex + i;
				if (index >= _log.Count)
					break;

				if (i > 0)
					content.Append(",");

				var item = _log[index];
				
				try
				{
					await _semaphore.WaitAsync();
					
					content.Append($"{{\"time\":{item._time},\"level\":\"{item._type}\",\"msg\":\"{Escape(item._message)}\",\"stack\":\"{Escape(item._stackTrace)}\"}}");
				}
				finally
				{
					_semaphore.Release();
				}
			}

			content.Append("]}");
			string result = content.ToString();
#if ZSTRING
			content.Dispose();
#endif
			return result;
		}

		private async void OnLogMessageReceived(string condition, string stackTrace, LogType type)
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
	}
}
