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
		private readonly int? _sessionID;

		private ILogFileSender _sender;
		private bool _sendInProgress;
		private bool _disposed;

		public SnipeLogPipeline(int? sessionID = null)
			: this(sessionID, Path.Combine(Application.temporaryCachePath, CACHE_DIRECTORY_NAME), null)
		{
		}

		internal SnipeLogPipeline(int? sessionID, string cacheDirectory, ILogFileSender sender, SnipeLogBufferLimits limits = null)
		{
			_sessionID = sessionID;
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

				if (!TryGetRecordSlice(record, _limits.MaxRecordBytes, out int messageChars, out int stackChars, out int bytes) ||
					!_buffer.CanAccept(bytes))
				{
					return;
				}
				var bounded = new SnipeLogRecord(record.Time, record.Level, Prefix(record.Message, messageChars), Prefix(record.StackTrace, stackChars));
				if (_buffer.Append(SerializeRecordToUtf8(bounded)) &&
					(messageChars < (record.Message?.Length ?? 0) || stackChars < (record.StackTrace?.Length ?? 0)))
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
					if (batch == null) return false;
					foreach (var entry in batch.Files)
					{
						long acknowledgedRecords = batch.GetAcknowledgedRecords(entry);
						if (acknowledgedRecords > 0 && new FileInfo(entry.Path).Length == 0)
							throw new InvalidDataException("Acknowledged log file was truncated to zero bytes.");
						bool success;
						using (var file = new StreamReader(entry.Path, SnipeLogFileBuffer.Utf8NoBom))
						{
							SkipAcknowledgedRecords(file, acknowledgedRecords);
							success = await sender.SendAsync(file, count => batch.Acknowledge(entry, count));
						}
						if (!success || !batch.DeleteSentFile(entry)) return false;
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
						DisposeBuffer();
					}
					else if (semaphoreOccupied) _buffer.EmitDiagnostics();
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
				DisposeBuffer();
			}
		}

		private void DisposeBuffer()
		{
			_buffer.Dispose();
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

		internal static byte[] SerializeLimitedRecord(SnipeLogRecord record, int maxBytes, out bool truncated)
		{
			truncated = false;
			if (!TryGetRecordSlice(record, maxBytes, out int messageChars, out int stackChars, out _)) return null;
			truncated = messageChars < (record.Message?.Length ?? 0) || stackChars < (record.StackTrace?.Length ?? 0);
			return SerializeRecordToUtf8(new SnipeLogRecord(record.Time, record.Level,
				Prefix(record.Message, messageChars), Prefix(record.StackTrace, stackChars)));
		}

		private static string Prefix(string value, int length)
		{
			return string.IsNullOrEmpty(value) || length == 0 ? string.Empty : length == value.Length ? value : value.Substring(0, length);
		}

		private static bool TryGetRecordSlice(SnipeLogRecord record, int maxBytes, out int messageChars, out int stackChars, out int bytes)
		{
			// Only the fixed envelope is serialized before admission; work on user fields
			// stops at the limit, even if a producer supplies a multi-megabyte snapshot.
			bytes = SnipeLogFileBuffer.Utf8NoBom.GetByteCount(SerializeRecord(new SnipeLogRecord(record.Time, record.Level, "", ""))) + 1;
			messageChars = stackChars = 0;
			if (bytes > maxBytes) return false;
			messageChars = TakeEscapedPrefix(record.Message, maxBytes, ref bytes);
			stackChars = TakeEscapedPrefix(record.StackTrace, maxBytes, ref bytes);
			return true;
		}

		private static int TakeEscapedPrefix(string value, int maxBytes, ref int bytes)
		{
			if (string.IsNullOrEmpty(value)) return 0;
			int index = 0;
			while (index < value.Length)
			{
				char c = value[index];
				int chars = 1;
				int cost;
				if (c == '"' || c == '\\' || c == '\b' || c == '\f' || c == '\n' || c == '\r' || c == '\t') cost = 2;
				else if (c < 0x20) cost = 6;
				else if (c < 0x80) cost = 1;
				else if (c < 0x800) cost = 2;
				else if (char.IsHighSurrogate(c) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
				{
					cost = 4;
					chars = 2;
				}
				else cost = 3; // BMP character or UTF8Encoding's replacement for an unpaired surrogate.
				if (cost > maxBytes - bytes) break;
				bytes += cost;
				index += chars;
			}
			return index;
		}
	}
}
