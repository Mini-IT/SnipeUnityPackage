using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Internal;
using MiniIT.Snipe.Logging;
using MiniIT.Threading;
using UnityEngine;

namespace MiniIT.Snipe
{
	public sealed class SnipeLogPipeline : ISnipeLogPipeline
	{
		public const string DiagnosticLogPrefix = "[SnipeLogPipeline]";

		private const string CACHE_DIRECTORY_NAME = "snipe-log-pipeline";

		private readonly object _stateLock = new object();
		private readonly AlterSemaphore _sendSemaphore = new AlterSemaphore(1, 1);
		private readonly SnipeLogFileBuffer _buffer;
		private readonly int? _sessionID;

		private ILogFileSender _sender;
		private bool _disposed;

		public SnipeLogPipeline(int? sessionID = null)
			: this(sessionID, Path.Combine(Application.temporaryCachePath, CACHE_DIRECTORY_NAME), null)
		{
		}

		internal SnipeLogPipeline(int? sessionID, string cacheDirectory, ILogFileSender sender)
		{
			_sessionID = sessionID;
			_sender = sender;
			_buffer = new SnipeLogFileBuffer(cacheDirectory);
		}

		public void Initialize(SnipeContext context, SnipeOptions options)
		{
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}

			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			lock (_stateLock)
			{
				if (_disposed)
				{
					throw new ObjectDisposedException(nameof(SnipeLogPipeline));
				}

				_sender = new LogSender(context, options, context.Communicator?.Services, _sessionID);
			}
		}

		public void Append(SnipeLogRecord record)
		{
			lock (_stateLock)
			{
				if (_disposed)
				{
					return;
				}

				_buffer.Append(SerializeRecordToUtf8(record));
			}
		}

		public async UniTask<bool> SendAsync()
		{
			bool semaphoreOccupied = false;

			try
			{
				await _sendSemaphore.WaitAsync();
				semaphoreOccupied = true;

				ILogFileSender sender;
				UniTask<bool> rotation;
				lock (_stateLock)
				{
					if (_disposed)
					{
						return false;
					}

					sender = _sender;
					if (sender == null)
					{
						DebugLogger.LogWarning($"{DiagnosticLogPrefix} Log pipeline is not initialized.");
						return false;
					}

					rotation = _buffer.RotateAsync();
				}

				if (!await rotation)
				{
					return false;
				}

				string[] filesToSend = _buffer.GetFilesReadyToSend();
				for (int i = 0; i < filesToSend.Length; i++)
				{
					string filePath = filesToSend[i];
					bool success;

					try
					{
						using (var file = new StreamReader(filePath, SnipeLogFileBuffer.Utf8NoBom))
						{
							success = await sender.SendAsync(file);
						}
					}
					catch (Exception ex)
					{
						DebugLogger.LogError($"{DiagnosticLogPrefix} Failed sending {filePath}: {LogUtil.GetReducedException(ex)}");
						return false;
					}

					if (!success || !_buffer.DeleteSentFile(filePath))
					{
						return false;
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"{DiagnosticLogPrefix} Send failed: {LogUtil.GetReducedException(ex)}");
				return false;
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_sendSemaphore.Release();
				}
			}
		}

		public void Dispose()
		{
			lock (_stateLock)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				_sender = null;
			}

			_buffer.Dispose();
		}

		internal string[] GetFilesReadyToSend()
		{
			return _buffer.GetFilesReadyToSend();
		}

		internal static bool IsDiagnosticLog(string message)
		{
			return !string.IsNullOrEmpty(message) &&
				message.StartsWith(DiagnosticLogPrefix, StringComparison.Ordinal);
		}

		internal static string SerializeRecord(SnipeLogRecord record)
		{
			var builder = new StringBuilder();
			builder.Append('{');
			builder.Append("\"time\":");
			builder.Append(record.Time);
			builder.Append(",\"level\":\"");
			builder.Append(EscapeJson(record.Level.ToString()));
			builder.Append("\",\"msg\":\"");
			builder.Append(EscapeJson(record.Message));
			builder.Append("\",\"stack\":\"");
			builder.Append(EscapeJson(record.StackTrace));
			builder.Append("\"}");
			return builder.ToString();
		}

		internal static string EscapeJson(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			var builder = new StringBuilder(value.Length + 8);
			for (int i = 0; i < value.Length; i++)
			{
				char character = value[i];
				switch (character)
				{
					case '"': builder.Append("\\\""); break;
					case '\\': builder.Append("\\\\"); break;
					case '\b': builder.Append("\\b"); break;
					case '\f': builder.Append("\\f"); break;
					case '\n': builder.Append("\\n"); break;
					case '\r': builder.Append("\\r"); break;
					case '\t': builder.Append("\\t"); break;
					default:
						if (character < 0x20)
						{
							builder.Append("\\u");
							builder.Append(((int)character).ToString("x4"));
						}
						else
						{
							builder.Append(character);
						}
						break;
				}
			}

			return builder.ToString();
		}

		private static byte[] SerializeRecordToUtf8(SnipeLogRecord record)
		{
			return SnipeLogFileBuffer.Utf8NoBom.GetBytes(SerializeRecord(record) + "\n");
		}
	}
}
