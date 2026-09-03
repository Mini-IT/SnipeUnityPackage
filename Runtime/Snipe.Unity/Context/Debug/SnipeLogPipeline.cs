using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Internal;
using MiniIT.Snipe.Logging;
using MiniIT.Threading;
using UnityEngine;

namespace MiniIT.Snipe
{
	public sealed class SnipeLogPipeline : IDisposable
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
			}

			_buffer.Append(SerializeRecord(record));
		}

		public async UniTask<bool> SendAsync()
		{
			bool semaphoreOccupied = false;

			try
			{
				await _sendSemaphore.WaitAsync();
				semaphoreOccupied = true;

				ILogFileSender sender;
				lock (_stateLock)
				{
					if (_disposed)
					{
						return false;
					}

					sender = _sender;
				}

				if (sender == null)
				{
					DebugLogger.LogWarning($"{DiagnosticLogPrefix} Log pipeline is not initialized.");
					return false;
				}

				if (!_buffer.Rotate())
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
	}

	internal sealed class SnipeLogFileBuffer : IDisposable
	{
		internal static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

		private const string FILE_PREFIX = "snipe-log-";
		private const string FILE_EXTENSION = ".ndjson";
		private const int MAX_RETAINED_STALE_FILES = 3;
		private const int MIN_BYTES_TO_FLUSH = 4096;

		private readonly object _syncRoot = new object();
		private readonly string _directoryPath;
		private readonly string _sessionFilePrefix;

		private StreamWriter _currentWriter;
		private string _currentFilePath;
		private int _bytesSinceFlush;
		private int _fileSequence;
		private long _currentFileLength;
		private bool _storageReady;
		private bool _disposed;

		internal SnipeLogFileBuffer(string directoryPath)
		{
			if (string.IsNullOrEmpty(directoryPath))
			{
				throw new ArgumentException("Log cache directory is required.", nameof(directoryPath));
			}

			_directoryPath = directoryPath;
			_sessionFilePrefix = $"{FILE_PREFIX}{Guid.NewGuid():N}-";
			EnsureStorageReady();
			CleanupStaleFiles();
		}

		internal bool Append(string line)
		{
			if (string.IsNullOrEmpty(line))
			{
				return false;
			}

			lock (_syncRoot)
			{
				if (_disposed || !EnsureStorageReady())
				{
					return false;
				}

				try
				{
					_currentWriter.WriteLine(line);
					int bytesWritten = Utf8NoBom.GetByteCount(line) + 1;
					_currentFileLength += bytesWritten;
					_bytesSinceFlush += bytesWritten;

					if (_bytesSinceFlush >= MIN_BYTES_TO_FLUSH)
					{
						_currentWriter.Flush();
						_bytesSinceFlush = 0;
					}

					return true;
				}
				catch (Exception ex)
				{
					DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to append log record: {LogUtil.GetReducedException(ex)}");
					CloseCurrentWriter();
					_storageReady = false;
					return false;
				}
			}
		}

		internal bool Rotate()
		{
			lock (_syncRoot)
			{
				if (_disposed || !EnsureStorageReady())
				{
					return false;
				}

				if (_currentFileLength <= 0)
				{
					return true;
				}

				try
				{
					CloseCurrentWriter();
					CreateCurrentFile();
					CleanupStaleFiles();
					return true;
				}
				catch (Exception ex)
				{
					DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to rotate log file: {LogUtil.GetReducedException(ex)}");
					CloseCurrentWriter();
					_storageReady = false;
					return false;
				}
			}
		}

		internal string[] GetFilesReadyToSend()
		{
			lock (_syncRoot)
			{
				if (!Directory.Exists(_directoryPath))
				{
					return Array.Empty<string>();
				}

				string[] files = Directory.GetFiles(_directoryPath, $"{_sessionFilePrefix}*{FILE_EXTENSION}");
				Array.Sort(files, StringComparer.Ordinal);
				var result = new List<string>(files.Length);

				for (int i = 0; i < files.Length; i++)
				{
					string filePath = files[i];
					if (PathEquals(filePath, _currentFilePath))
					{
						continue;
					}

					if (new FileInfo(filePath).Length == 0)
					{
						TryDeleteFile(filePath);
						continue;
					}

					result.Add(filePath);
				}

				return result.ToArray();
			}
		}

		internal bool DeleteSentFile(string filePath)
		{
			lock (_syncRoot)
			{
				bool deleted = TryDeleteFile(filePath);
				CleanupStaleFiles();
				return deleted;
			}
		}

		public void Dispose()
		{
			lock (_syncRoot)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				CloseCurrentWriter();
				if (_currentFileLength == 0)
				{
					TryDeleteFile(_currentFilePath);
				}

				CleanupStaleFiles();
			}
		}

		private bool EnsureStorageReady()
		{
			if (_storageReady)
			{
				return true;
			}

			try
			{
				Directory.CreateDirectory(_directoryPath);
				CreateCurrentFile();
				_storageReady = true;
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to initialize log storage: {LogUtil.GetReducedException(ex)}");
				_storageReady = false;
				return false;
			}
		}

		private void CreateCurrentFile()
		{
			string fileName = $"{_sessionFilePrefix}{_fileSequence++:D8}-{Guid.NewGuid():N}{FILE_EXTENSION}";
			_currentFilePath = Path.Combine(_directoryPath, fileName);
			var stream = new FileStream(_currentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			_currentWriter = new StreamWriter(stream, Utf8NoBom)
			{
				NewLine = "\n"
			};
			_bytesSinceFlush = 0;
			_currentFileLength = 0;
		}

		private void CloseCurrentWriter()
		{
			if (_currentWriter == null)
			{
				return;
			}

			try
			{
				_currentWriter.Flush();
			}
			catch (Exception ex)
			{
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to flush log file: {LogUtil.GetReducedException(ex)}");
			}

			try
			{
				_currentWriter.Dispose();
			}
			catch (Exception ex)
			{
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to close log file: {LogUtil.GetReducedException(ex)}");
			}
			finally
			{
				_currentWriter = null;
				_bytesSinceFlush = 0;
			}
		}

		private void CleanupStaleFiles()
		{
			if (!Directory.Exists(_directoryPath))
			{
				return;
			}

			string[] files = Directory.GetFiles(_directoryPath, $"{FILE_PREFIX}*{FILE_EXTENSION}");
			var staleFiles = new List<FileInfo>();
			for (int i = 0; i < files.Length; i++)
			{
				string filePath = files[i];
				if (PathEquals(filePath, _currentFilePath) ||
					Path.GetFileName(filePath).StartsWith(_sessionFilePrefix, StringComparison.Ordinal))
				{
					continue;
				}

				staleFiles.Add(new FileInfo(filePath));
			}

			staleFiles.Sort((left, right) =>
			{
				int comparison = DateTime.Compare(right.LastWriteTimeUtc, left.LastWriteTimeUtc);
				return comparison != 0 ? comparison : string.CompareOrdinal(left.FullName, right.FullName);
			});

			for (int i = MAX_RETAINED_STALE_FILES; i < staleFiles.Count; i++)
			{
				TryDeleteFile(staleFiles[i].FullName);
			}
		}

		private static bool TryDeleteFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return true;
			}

			try
			{
				File.Delete(filePath);
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to delete {filePath}: {LogUtil.GetReducedException(ex)}");
				return false;
			}
		}

		private static bool PathEquals(string left, string right)
		{
			return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
		}
	}
}
