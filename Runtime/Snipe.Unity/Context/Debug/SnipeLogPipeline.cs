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
	/// <summary>
	/// Buffers logs for this instance's lifetime. Unsent files and acknowledgement progress
	/// are discarded on disposal and are not recovered by subsequent instances.
	/// Oversized records are truncated; new records can be dropped when the bounded
	/// queue or cache is full. SendAsync reports delivery of its selected snapshot.
	/// </summary>
	public sealed class SnipeLogPipeline : ISnipeLogPipeline
	{
		public const string DIAGNOSTIC_LOG_PREFIX = "[SnipeLogPipeline]";

		private const string CACHE_DIRECTORY_NAME = "snipe-log-pipeline";

		private readonly object _stateLock = new object();
		private readonly AlterSemaphore _sendSemaphore = new AlterSemaphore(1, 1);
		private readonly SnipeLogFileBuffer _buffer;
		private readonly SnipeLogBufferLimits _limits;
		private readonly int? _sessionId;

		private ILogFileSender _sender;
		private bool _sendInProgress;
		private bool _disposed;

		public SnipeLogPipeline(int? sessionId = null)
			: this(sessionId, Path.Combine(Application.temporaryCachePath, CACHE_DIRECTORY_NAME), null)
		{
		}

		internal SnipeLogPipeline(int? sessionId, string cacheDirectory, ILogFileSender sender, SnipeLogBufferLimits limits = null)
		{
			_sessionId = sessionId;
			_sender = sender;
			_limits = limits ?? SnipeLogBufferLimits.Default;
			_buffer = new SnipeLogFileBuffer(cacheDirectory, limits: _limits);
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

				_sender = new LogSender(context, options, context.Communicator?.Services, _sessionId);
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

				if (!TryGetRecordSlice(record, _limits.MaxRecordBytes, out int messageCharCount, out int stackTraceCharCount, out int byteCount) ||
					!_buffer.CanAccept(byteCount))
				{
					return;
				}
				var bounded = new SnipeLogRecord(record.TimestampMilliseconds, record.Level,
					Prefix(record.Message, messageCharCount), Prefix(record.StackTrace, stackTraceCharCount));
				if (_buffer.Append(SerializeRecordToUtf8(bounded)) &&
					(messageCharCount < (record.Message?.Length ?? 0) || stackTraceCharCount < (record.StackTrace?.Length ?? 0)))
				{
					_buffer.NoteTruncation();
				}
			}
		}

		public async UniTask<bool> SendAsync()
		{
			bool semaphoreOccupied = false;
			bool started = false;

			try
			{
				await _sendSemaphore.WaitAsync();
				semaphoreOccupied = true;

				ILogFileSender sender;
				UniTask<SnipeLogCacheDirectory.SendBatch> fileSeal;
				lock (_stateLock)
				{
					if (_disposed)
					{
						return false;
					}

					sender = _sender;
					fileSeal = default;
					if (sender != null)
					{
						_sendInProgress = true;
						started = true;
						fileSeal = _buffer.SealCurrentFileAsync();
					}
				}
				if (sender == null)
				{
					DebugLogger.LogWarning($"{DIAGNOSTIC_LOG_PREFIX} Log pipeline is not initialized.");
					return false;
				}

				using (var batch = await fileSeal)
				{
					if (batch == null)
					{
						return false;
					}
					foreach (var entry in batch.Files)
					{
						long acknowledgedRecords = batch.GetAcknowledgedRecords(entry);
						if (acknowledgedRecords > 0 && new FileInfo(entry.Path).Length == 0)
						{
							throw new InvalidDataException("Acknowledged log file was truncated to zero bytes.");
						}
						bool success;
						using (var file = new StreamReader(entry.Path, SnipeLogFileBuffer.Utf8NoBom))
						{
							SkipAcknowledgedRecords(file, acknowledgedRecords);
							success = await sender.SendAsync(file, count => batch.Acknowledge(entry, count));
						}
						if (!success || !batch.DeleteSentFile(entry))
						{
							return false;
						}
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"{DIAGNOSTIC_LOG_PREFIX} Send failed: {LogUtil.GetReducedException(ex)}");
				return false;
			}
			finally
			{
				try
				{
					bool disposeBuffer = false;
					if (started)
					{
						lock (_stateLock)
						{
							_sendInProgress = false;
							disposeBuffer = _disposed;
						}
					}

					if (disposeBuffer)
					{
						_buffer.Dispose();
					}
					else if (semaphoreOccupied)
					{
						_buffer.EmitDiagnostics();
					}
				}
				finally
				{
					if (semaphoreOccupied)
					{
						_sendSemaphore.Release();
					}
				}
			}
		}

		private static void SkipAcknowledgedRecords(StreamReader file, long acknowledgedRecords)
		{
			while (acknowledgedRecords > 0)
			{
				string line = file.ReadLine();
				if (line == null)
				{
					throw new InvalidDataException("Log file contains fewer records than its acknowledged progress.");
				}

				if (line.Length > 0)
				{
					acknowledgedRecords--;
				}
			}
		}

		/// <summary>
		/// Stops accepting logs and new sends. An active send is allowed to finish before
		/// its files are deleted; this method does not wait for that network operation.
		/// </summary>
		public void Dispose()
		{
			bool disposeBuffer;
			lock (_stateLock)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				_sender = null;
				disposeBuffer = !_sendInProgress;
			}

			if (disposeBuffer)
			{
				_buffer.Dispose();
			}
		}

		internal static bool IsDiagnosticLog(string message)
		{
			return !string.IsNullOrEmpty(message) &&
				message.StartsWith(DIAGNOSTIC_LOG_PREFIX, StringComparison.Ordinal);
		}

		internal static string SerializeRecord(SnipeLogRecord record)
		{
			var builder = new StringBuilder();
			builder.Append('{');
			builder.Append("\"time\":");
			builder.Append(record.TimestampMilliseconds);
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

		private static string Prefix(string value, int length)
		{
			return string.IsNullOrEmpty(value) || length == 0 ? string.Empty : length == value.Length ? value : value.Substring(0, length);
		}

		internal static bool TryGetRecordSlice(SnipeLogRecord record, int maxBytes,
			out int messageCharCount, out int stackTraceCharCount, out int byteCount)
		{
			// Only the fixed envelope is serialized before admission; work on user fields
			// stops at the limit, even if a producer supplies a multi-megabyte snapshot.
			byteCount = SnipeLogFileBuffer.Utf8NoBom.GetByteCount(
				SerializeRecord(new SnipeLogRecord(record.TimestampMilliseconds, record.Level, "", ""))) + 1;
			messageCharCount = stackTraceCharCount = 0;
			if (byteCount > maxBytes)
			{
				return false;
			}
			messageCharCount = TakeEscapedPrefix(record.Message, maxBytes, ref byteCount);
			stackTraceCharCount = TakeEscapedPrefix(record.StackTrace, maxBytes, ref byteCount);
			return true;
		}

		private static int TakeEscapedPrefix(string value, int maxBytes, ref int byteCount)
		{
			if (string.IsNullOrEmpty(value))
			{
				return 0;
			}
			int index = 0;
			while (index < value.Length)
			{
				char character = value[index];
				int characterCount = 1;
				int characterByteCount;
				if (character == '"' || character == '\\' || character == '\b' || character == '\f' ||
					character == '\n' || character == '\r' || character == '\t')
				{
					characterByteCount = 2;
				}
				else if (character < 0x20)
				{
					characterByteCount = 6;
				}
				else if (character < 0x80)
				{
					characterByteCount = 1;
				}
				else if (character < 0x800)
				{
					characterByteCount = 2;
				}
				else if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
				{
					characterByteCount = 4;
					characterCount = 2;
				}
				else
				{
					// BMP character or UTF8Encoding's replacement for an unpaired surrogate.
					characterByteCount = 3;
				}
				if (characterByteCount > maxBytes - byteCount)
				{
					break;
				}
				byteCount += characterByteCount;
				index += characterCount;
			}
			return index;
		}
	}
}
