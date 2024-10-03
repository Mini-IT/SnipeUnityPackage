using System;
using MiniIT.Snipe;
using MiniIT.Snipe.Internal;
using MiniIT.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.IO;
using System.Text;

namespace MiniIT
{
	public class LogReporter : IDisposable
	{
		private static readonly AlterSemaphore s_semaphore = new AlterSemaphore(1, 1);

		private readonly LogSender _sender;

		private static string _filePath;
		private static FileStream _file;

		static LogReporter()
		{
			Application.logMessageReceivedThreaded += OnLogMessageReceived;
			CreateNewFile();
		}

		private static void CreateNewFile()
		{
			_file?.Close();

			long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			_filePath = Path.Combine(UnityEngine.Application.temporaryCachePath, $"log{ts}.txt");
			_file = File.Open(_filePath, FileMode.OpenOrCreate, FileAccess.Write);

			DebugLogger.Log($"[{nameof(LogReporter)}] New temp log file created: " + _filePath);
		}

		public LogReporter()
		{
			_sender = new LogSender(s_semaphore);
		}

		internal void SetSnipeContext(SnipeContext snipeContext)
		{
			_sender.SetSnipeContext(snipeContext);
		}

		public async UniTask<bool> SendAsync()
		{
			bool result = false;
			string filepath = null;

			bool semaphoreOccupied = false;

			try
			{
				await s_semaphore.WaitAsync();
				semaphoreOccupied = true;

				filepath = _filePath;
				CreateNewFile();
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"[{nameof(LogReporter)}] " + ex.ToString());
			}
			finally
			{
				if (semaphoreOccupied)
				{
					s_semaphore.Release();
				}
			}

			StreamReader file = null;

			try
			{
				file = File.OpenText(filepath);

				result = await _sender.SendAsync(file);
			}
			finally
			{
				file?.Dispose();

				if (!string.IsNullOrEmpty(filepath) && File.Exists(filepath))
				{
					try
					{
						File.Delete(filepath);
						DebugLogger.Log($"[{nameof(LogReporter)}] Temp log file deleted " + filepath);
					}
					catch (Exception e)
					{
						DebugLogger.LogError($"[{nameof(LogReporter)}] Failed deleting temp log file: " + e.ToString());
					}
				}
			}

			return result;
		}

		private static async void OnLogMessageReceived(string condition, string stackTrace, LogType type)
		{
			bool semaphoreOccupied = false;

			try
			{
				await s_semaphore.WaitAsync();
				semaphoreOccupied = true;

				if (_file != null && _file.CanWrite)
				{
					long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					string json = GetLogRecordJson(ts, type, condition, stackTrace);
					byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
					await _file.WriteAsync(bytes, 0, bytes.Length);
				}
			}
			finally
			{
				if (semaphoreOccupied)
				{
					s_semaphore.Release();
				}
			}
		}

		private static string GetLogRecordJson(long time, LogType type, string message, string stackTrace)
		{
			return string.Format("{{\"time\":{0},\"level\":\"{1}\",\"msg\":\"{2}\",\"stack\":\"{3}\"}}",
				time,
				type,
				Escape(message),
				Escape(stackTrace));
		}

		// https://stackoverflow.com/a/14087738
		private static string Escape(string input)
		{
			var literal = new StringBuilder(input.Length + 2);
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
			_file?.Dispose();
			_file = null;
			_sender.Dispose();

			try
			{
				if (File.Exists(_filePath))
				{
					File.Delete(_filePath);
					DebugLogger.Log($"[{nameof(LogReporter)}] File {_filePath} deleted");
				}
			}
			catch (Exception e)
			{
				DebugLogger.LogError($"[{nameof(LogReporter)}] Failed to delete {_filePath}: " + e.ToString());
			}
		}
	}
}
