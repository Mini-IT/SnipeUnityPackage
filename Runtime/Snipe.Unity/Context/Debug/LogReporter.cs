#if UNITY_WEBGL
#define SINGLE_THREAD
#endif

using System;
using MiniIT.Snipe;
using MiniIT.Snipe.Internal;
using MiniIT.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.IO;
using System.Text;
using MiniIT.Snipe.Logging;

namespace MiniIT
{
	public class LogReporter : IDisposable
	{
		private const int MIN_BYTES_TO_FLUSH = 4096;

		private static readonly AlterSemaphore s_semaphore;

		private static string s_filePath;
		private static FileStream s_file;
		private static int s_bytesWritten;
		private SnipeContext _snipeContext;

		private static bool s_canWrite = true;

		static LogReporter()
		{
			s_semaphore = new AlterSemaphore(1, 1);
			Application.logMessageReceivedThreaded += OnLogMessageReceived;
			CreateNewFile();
		}

		private static void CreateNewFile()
		{
			try
			{
				// Close the previous file, if it exists
				if (s_file != null)
				{
					try
					{
						s_file.Close();
						s_file = null;
					}
					catch (Exception ex)
					{
						DebugLogger.LogWarning($"[{nameof(LogReporter)}] Error closing previous log file: {ex.Message}");
					}
				}

				// Check if there is a directory for logs
				string cacheDirectory = Application.temporaryCachePath;
				if (!Directory.Exists(cacheDirectory))
				{
					try
					{
						Directory.CreateDirectory(cacheDirectory);
					}
					catch (Exception ex)
					{
						DebugLogger.LogError($"[{nameof(LogReporter)}] Failed to create cache directory: {ex.Message}");
						s_canWrite = false;
						return;
					}
				}

				long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				s_filePath = Path.Combine(cacheDirectory, $"log{ts}.txt");

				try
				{
					s_file = File.Open(s_filePath, FileMode.OpenOrCreate, FileAccess.Write);
					s_bytesWritten = 0;
					DebugLogger.Log($"[{nameof(LogReporter)}] New temp log file created: {s_filePath}");
				}
				catch (IOException ioEx)
				{
					// I/O error handling, including full disk drive
					DebugLogger.LogError($"[{nameof(LogReporter)}] IO Error creating log file: {ioEx.Message}");
					s_canWrite = false;
				}
				catch (UnauthorizedAccessException uaEx)
				{
					// Access error handling
					DebugLogger.LogError($"[{nameof(LogReporter)}] Access denied when creating log file: {uaEx.Message}");
					s_canWrite = false;
				}
				catch (Exception ex)
				{
					// Handling other errors
					DebugLogger.LogError($"[{nameof(LogReporter)}] Error creating log file: {ex.Message}");
					s_canWrite = false;
				}
			}
			catch (Exception ex)
			{
				// Handling any unforeseen errors
				DebugLogger.LogError($"[{nameof(LogReporter)}] Unexpected error in CreateNewFile: {ex.Message}");
				s_canWrite = false;
			}
		}

		internal void SetSnipeContext(SnipeContext snipeContext)
		{
			_snipeContext = snipeContext;
		}

		public async UniTask<bool> SendAsync()
		{
			if (!s_canWrite)
			{
				return false;
			}

			bool result = false;
			string filepath = null;

			bool semaphoreOccupied = false;

			try
			{
				await s_semaphore.WaitAsync();
				semaphoreOccupied = true;

				filepath = s_filePath;
				CreateNewFile();
			}
			catch (Exception ex)
			{
				string exceptionMessage = LogUtil.GetReducedException(ex);
				DebugLogger.LogError($"[{nameof(LogReporter)}] {exceptionMessage}");
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
				var sender = new LogSender(_snipeContext);
				result = await sender.SendAsync(file);
			}
			catch (Exception ex)
			{
				string exceptionMessage = LogUtil.GetReducedException(ex);
				DebugLogger.LogError($"[{nameof(LogReporter)}] {exceptionMessage}");
			}
			finally
			{
				try
				{
					file?.Dispose();
				}
				catch (Exception)
				{
					// Ignore
				}

				if (!string.IsNullOrEmpty(filepath) && File.Exists(filepath))
				{
					try
					{
						File.Delete(filepath);
						DebugLogger.Log($"[{nameof(LogReporter)}] Temp log file deleted {filepath}");
					}
					catch (Exception e)
					{
						string exceptionMessage = LogUtil.GetReducedException(e);
						DebugLogger.LogError($"[{nameof(LogReporter)}] Failed deleting temp log file: {exceptionMessage}");
					}
				}
			}

			return result;
		}

		private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
		{
			if (!s_canWrite)
			{
				StaticDispose();
				return;
			}

			ProcessLogMessageAsync(condition, stackTrace, type).Forget();
		}

		private static async UniTaskVoid ProcessLogMessageAsync(string condition, string stackTrace, LogType type)
		{
			bool semaphoreOccupied = false;

			try
			{
				await s_semaphore.WaitAsync();
				semaphoreOccupied = true;

				if (s_file != null && s_file.CanWrite)
				{
					try
					{
						long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						string json = GetLogRecordJson(ts, type, condition, stackTrace);
						byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");

#if SINGLE_THREAD
						s_file.Write(bytes, 0, bytes.Length);
#else
						await s_file.WriteAsync(bytes, 0, bytes.Length);
#endif

						s_bytesWritten += bytes.Length;
						if (s_bytesWritten >= MIN_BYTES_TO_FLUSH)
						{
							s_bytesWritten = 0;
#if SINGLE_THREAD
							s_file.Flush();
#else
							await s_file.FlushAsync();
#endif
						}
					}
					catch (IOException ioEx)
					{
						// I/O error handling, including full disk drive
						DebugLogger.LogError($"[{nameof(LogReporter)}] IO Error writing to log file: {ioEx.Message}");
						s_canWrite = false;
						StaticDispose();
					}
					catch (Exception ex)
					{
						// Handling other errors
						DebugLogger.LogError($"[{nameof(LogReporter)}] Error writing to log file: {ex.Message}");
						s_canWrite = false;
						StaticDispose();
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"[{nameof(LogReporter)}] Error in ProcessLogMessageAsync: {ex.Message}");
				s_canWrite = false;
				StaticDispose();
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
			StaticDispose();
		}

		private static void StaticDispose()
		{
			try
			{
				if (s_file != null)
				{
					try
					{
						s_file.Dispose();
					}
					catch (Exception ex)
					{
						DebugLogger.LogWarning($"[{nameof(LogReporter)}] Error disposing log file: {ex.Message}");
					}
					finally
					{
						s_file = null;
					}
				}

				if (!string.IsNullOrEmpty(s_filePath) && File.Exists(s_filePath))
				{
					try
					{
						File.Delete(s_filePath);
						DebugLogger.Log($"[{nameof(LogReporter)}] File {s_filePath} deleted");
					}
					catch (Exception e)
					{
						string exceptionMessage = LogUtil.GetReducedException(e);
						DebugLogger.LogError($"[{nameof(LogReporter)}] Failed to delete {s_filePath}: {exceptionMessage}");
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"[{nameof(LogReporter)}] Error in StaticDispose: {ex.Message}");
			}
			finally
			{
				Application.logMessageReceivedThreaded -= OnLogMessageReceived;
			}
		}
	}
}
