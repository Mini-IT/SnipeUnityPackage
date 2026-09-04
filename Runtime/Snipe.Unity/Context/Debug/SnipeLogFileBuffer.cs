using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Logging;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Threading;
using System.Threading.Tasks;
#endif

namespace MiniIT.Snipe.Internal
{
	internal sealed class SnipeLogFileBuffer : IDisposable
	{
		internal static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

		private const string FILE_PREFIX = "snipe-log-";
		private const string FILE_EXTENSION = ".ndjson";
		private const int MAX_RETAINED_STALE_FILES = 3;
		private const int MIN_BYTES_TO_FLUSH = 4096;
#if UNITY_WEBGL && !UNITY_EDITOR
		private const int WEB_GL_BYTES_PER_FRAME = 4096;
#endif

		private readonly object _queueLock = new object();
		private readonly object _fileSystemLock = new object();
		private readonly Queue<SnipeLogBufferCommand> _commands = new Queue<SnipeLogBufferCommand>();
		private readonly string _directoryPath;
		private readonly string _sessionFilePrefix;
#if UNITY_WEBGL && !UNITY_EDITOR
		private bool _webGlPumpRunning;
		private bool _webGlPumpStopped;
		private SnipeLogBufferCommand _webGlActiveCommand;
#else
		private readonly SemaphoreSlim _commandSignal = new SemaphoreSlim(0);
		private readonly Task _writerLoop;
#endif

		private FileStream _currentWriter;
		private string _currentFilePath;
		private int _bytesSinceFlush;
		private int _fileSequence;
		private long _currentFileLength;
		private bool _storageReady;
		private bool _accepting = true;
		private bool _disposed;

		internal SnipeLogFileBuffer(string directoryPath)
		{
			if (string.IsNullOrEmpty(directoryPath))
			{
				throw new ArgumentException("Log cache directory is required.", nameof(directoryPath));
			}

			_directoryPath = directoryPath;
			_sessionFilePrefix = $"{FILE_PREFIX}{Guid.NewGuid():N}-";
			CleanupStaleFiles();
#if !UNITY_WEBGL || UNITY_EDITOR
			_writerLoop = Task.Run(ProcessQueueAsync);
#endif
		}

		internal bool Append(byte[] record)
		{
			if (record == null || record.Length == 0)
			{
				return false;
			}

			return TryEnqueue(SnipeLogBufferCommand.Append(record));
		}

		internal async UniTask<bool> RotateAsync()
		{
			SnipeLogBufferCommand command = SnipeLogBufferCommand.Rotate();
			if (!TryEnqueue(command))
			{
				return false;
			}

			return await command.Completion.Task;
		}

		internal string[] GetFilesReadyToSend()
		{
			lock (_fileSystemLock)
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
			bool deleted;
			lock (_fileSystemLock)
			{
				deleted = TryDeleteFile(filePath);
			}

			CleanupStaleFiles();
			return deleted;
		}

		public void Dispose()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			lock (_queueLock)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				_accepting = false;
				_webGlPumpStopped = true;
			}

			DrainWebGlQueueSynchronously();
			CloseCurrentWriterSynchronously();
			DeleteEmptyCurrentFile();
			CleanupStaleFiles();
#else
			SnipeLogBufferCommand stopCommand;
			lock (_queueLock)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				_accepting = false;
				stopCommand = SnipeLogBufferCommand.Stop();
				_commands.Enqueue(stopCommand);
			}

			_commandSignal.Release();
			try
			{
				stopCommand.Completion.Task.GetAwaiter().GetResult();
				_writerLoop.GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to stop log writer: {LogUtil.GetReducedException(ex)}");
			}

			_commandSignal.Dispose();
#endif
		}

		private bool TryEnqueue(SnipeLogBufferCommand command)
		{
			lock (_queueLock)
			{
				if (!_accepting)
				{
					return false;
				}

				_commands.Enqueue(command);
			}

			SignalWriter();
			return true;
		}

		private SnipeLogBufferCommand DequeueCommand()
		{
			lock (_queueLock)
			{
				return _commands.Count > 0 ? _commands.Dequeue() : null;
			}
		}

		private void SignalWriter()
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			bool startPump = false;
			lock (_queueLock)
			{
				if (!_webGlPumpRunning && !_webGlPumpStopped)
				{
					_webGlPumpRunning = true;
					startPump = true;
				}
			}

			if (startPump)
			{
				ProcessWebGlQueueAsync().Forget();
			}
#else
			_commandSignal.Release();
#endif
		}

#if !UNITY_WEBGL || UNITY_EDITOR
		private async Task ProcessQueueAsync()
		{
			while (true)
			{
				await _commandSignal.WaitAsync().ConfigureAwait(false);
				SnipeLogBufferCommand command = DequeueCommand();
				if (command == null)
				{
					continue;
				}

				try
				{
					switch (command.Type)
					{
						case SnipeLogBufferCommandType.Append:
							await AppendRecordAsync(command.Data).ConfigureAwait(false);
							break;
						case SnipeLogBufferCommandType.Rotate:
							command.Completion.TrySetResult(await RotateStorageAsync().ConfigureAwait(false));
							break;
						case SnipeLogBufferCommandType.Stop:
							await CloseCurrentWriterAsync().ConfigureAwait(false);
							DeleteEmptyCurrentFile();
							CleanupStaleFiles();
							command.Completion.TrySetResult(true);
							return;
					}
				}
				catch (Exception ex)
				{
					DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Log writer command failed: {LogUtil.GetReducedException(ex)}");
					command.Completion?.TrySetResult(false);
					if (command.Type == SnipeLogBufferCommandType.Stop)
					{
						return;
					}
				}
			}
		}

		private async Task<bool> AppendRecordAsync(byte[] record)
		{
			if (!EnsureStorageReady())
			{
				return false;
			}

			try
			{
				await _currentWriter.WriteAsync(record, 0, record.Length).ConfigureAwait(false);
				_currentFileLength += record.Length;
				_bytesSinceFlush += record.Length;

				if (_bytesSinceFlush >= MIN_BYTES_TO_FLUSH)
				{
					await _currentWriter.FlushAsync().ConfigureAwait(false);
					_bytesSinceFlush = 0;
				}

				return true;
			}
			catch (Exception ex)
			{
				HandleAppendFailure(ex);
				return false;
			}
		}

		private async Task<bool> RotateStorageAsync()
		{
			if (!EnsureStorageReady())
			{
				return false;
			}

			if (_currentFileLength <= 0)
			{
				return true;
			}

			if (!await CloseCurrentWriterAsync().ConfigureAwait(false))
			{
				_storageReady = false;
				return false;
			}

			try
			{
				CreateCurrentFile();
				CleanupStaleFiles();
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to rotate log file: {LogUtil.GetReducedException(ex)}");
				_storageReady = false;
				return false;
			}
		}

		private async Task<bool> CloseCurrentWriterAsync()
		{
			if (_currentWriter == null)
			{
				return true;
			}

			bool success = true;
			try
			{
				await _currentWriter.FlushAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				success = false;
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to flush log file: {LogUtil.GetReducedException(ex)}");
			}

			try
			{
				_currentWriter.Dispose();
			}
			catch (Exception ex)
			{
				success = false;
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to close log file: {LogUtil.GetReducedException(ex)}");
			}
			finally
			{
				_currentWriter = null;
				_bytesSinceFlush = 0;
			}

			return success;
		}
#else
		private async UniTaskVoid ProcessWebGlQueueAsync()
		{
			await UniTask.Yield(PlayerLoopTiming.Update);
			while (true)
			{
				SnipeLogBufferCommand command;
				lock (_queueLock)
				{
					if (_webGlPumpStopped)
					{
						_webGlPumpRunning = false;
						return;
					}

					if (_webGlActiveCommand == null && _commands.Count > 0)
					{
						_webGlActiveCommand = _commands.Dequeue();
					}

					command = _webGlActiveCommand;
					if (command == null)
					{
						_webGlPumpRunning = false;
						return;
					}
				}

				if (ProcessWebGlCommand(command, WEB_GL_BYTES_PER_FRAME))
				{
					lock (_queueLock)
					{
						if (ReferenceEquals(_webGlActiveCommand, command))
						{
							_webGlActiveCommand = null;
						}
					}
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}
		}

		private bool ProcessWebGlCommand(SnipeLogBufferCommand command, int maxBytes)
		{
			switch (command.Type)
			{
				case SnipeLogBufferCommandType.Append:
					return AppendRecordChunk(command, maxBytes);
				case SnipeLogBufferCommandType.Rotate:
					command.Completion.TrySetResult(RotateStorageSynchronously());
					return true;
				default:
					return true;
			}
		}

		private bool AppendRecordChunk(SnipeLogBufferCommand command, int maxBytes)
		{
			if (!EnsureStorageReady())
			{
				return true;
			}

			try
			{
				int remaining = command.Data.Length - command.Offset;
				int bytesToWrite = Math.Min(remaining, maxBytes);
				_currentWriter.Write(command.Data, command.Offset, bytesToWrite);
				command.Offset += bytesToWrite;
				_currentFileLength += bytesToWrite;
				_bytesSinceFlush += bytesToWrite;

				if (_bytesSinceFlush >= MIN_BYTES_TO_FLUSH)
				{
					_currentWriter.Flush();
					_bytesSinceFlush = 0;
				}

				return command.Offset >= command.Data.Length;
			}
			catch (Exception ex)
			{
				HandleAppendFailure(ex);
				return true;
			}
		}

		private bool RotateStorageSynchronously()
		{
			if (!EnsureStorageReady())
			{
				return false;
			}

			if (_currentFileLength <= 0)
			{
				return true;
			}

			if (!CloseCurrentWriterSynchronously())
			{
				_storageReady = false;
				return false;
			}

			try
			{
				CreateCurrentFile();
				CleanupStaleFiles();
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to rotate log file: {LogUtil.GetReducedException(ex)}");
				_storageReady = false;
				return false;
			}
		}

		private void DrainWebGlQueueSynchronously()
		{
			while (true)
			{
				SnipeLogBufferCommand command;
				lock (_queueLock)
				{
					command = _webGlActiveCommand;
					_webGlActiveCommand = null;
					if (command == null && _commands.Count > 0)
					{
						command = _commands.Dequeue();
					}
				}

				if (command == null)
				{
					return;
				}

				while (!ProcessWebGlCommand(command, int.MaxValue))
				{
				}
			}
		}

		private bool CloseCurrentWriterSynchronously()
		{
			if (_currentWriter == null)
			{
				return true;
			}

			bool success = true;
			try
			{
				_currentWriter.Flush();
			}
			catch (Exception ex)
			{
				success = false;
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to flush log file: {LogUtil.GetReducedException(ex)}");
			}

			try
			{
				_currentWriter.Dispose();
			}
			catch (Exception ex)
			{
				success = false;
				DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to close log file: {LogUtil.GetReducedException(ex)}");
			}
			finally
			{
				_currentWriter = null;
				_bytesSinceFlush = 0;
			}

			return success;
		}
#endif

		private bool EnsureStorageReady()
		{
			if (_storageReady && _currentWriter != null)
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
#if UNITY_WEBGL && !UNITY_EDITOR
			_currentWriter = new FileStream(_currentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, false);
#else
			_currentWriter = new FileStream(_currentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true);
#endif
			_bytesSinceFlush = 0;
			_currentFileLength = 0;
			_storageReady = true;
		}

		private void HandleAppendFailure(Exception exception)
		{
			DebugLogger.LogError($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to append log record: {LogUtil.GetReducedException(exception)}");
#if UNITY_WEBGL && !UNITY_EDITOR
			CloseCurrentWriterSynchronously();
#else
			try
			{
				_currentWriter?.Dispose();
			}
			catch (Exception closeException)
			{
				DebugLogger.LogWarning(
					$"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to close log file: {LogUtil.GetReducedException(closeException)}");
			}
			finally
			{
				_currentWriter = null;
				_bytesSinceFlush = 0;
			}
#endif
			_storageReady = false;
		}

		private void DeleteEmptyCurrentFile()
		{
			if (_currentFileLength != 0)
			{
				return;
			}

			lock (_fileSystemLock)
			{
				TryDeleteFile(_currentFilePath);
			}
		}

		private void CleanupStaleFiles()
		{
			lock (_fileSystemLock)
			{
				try
				{
					CleanupStaleFilesUnsafe();
				}
				catch (Exception ex)
				{
					DebugLogger.LogWarning($"{SnipeLogPipeline.DiagnosticLogPrefix} Failed to clean stale log files: {LogUtil.GetReducedException(ex)}");
				}
			}
		}

		private void CleanupStaleFilesUnsafe()
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
