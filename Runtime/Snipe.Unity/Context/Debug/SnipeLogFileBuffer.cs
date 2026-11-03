using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	internal sealed class SnipeLogFileBuffer : IDisposable
	{
		internal static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
		private const int MIN_BYTES_TO_FLUSH = 4096;
		private const int WEB_GL_BYTES_PER_FRAME = 4096;
		private const int WEB_GL_COMMANDS_PER_FRAME = 64;
		private readonly object _queueLock = new object();
		private readonly Queue<SnipeLogBufferCommand> _commands = new Queue<SnipeLogBufferCommand>();
		private readonly string _directoryPath;
		private readonly string _owner;
		private readonly SnipeLogBufferLimits _limits;
		private readonly SnipeLogCacheDirectory _cache;
		private readonly bool _frameWriter;
		private readonly bool _manualWriter;
#if !UNITY_WEBGL || UNITY_EDITOR
		private readonly SemaphoreSlim _commandSignal = new SemaphoreSlim(0);
		private readonly Task _writerLoop;
#endif
		private FileStream _currentWriter;
		private SnipeLogCacheDirectory.FileEntry _currentFile;
		private SnipeLogBufferCommand _frameActiveCommand;
		private int _queuedBytes;
		private int _fileSequence;
		private int _currentFileLength;
		private int _bytesSinceFlush;
		private bool _pumpRunning;
		private bool _accepting = true;
		private bool _disposed;
		private long _truncatedRecords;
		private long _queueDrops;
		private long _quotaDrops;
		private long _evictedRecords;
		private long _writeFailures;

		internal int QueuedBytes { get { lock (_queueLock) return _queuedBytes; } }
		internal SnipeLogCacheDirectory Cache => _cache;

		internal SnipeLogFileBuffer(string directoryPath, Task writerStartGate = null,
			SnipeLogBufferLimits limits = null, bool startWriter = true)
		{
			if (string.IsNullOrEmpty(directoryPath))
				throw new ArgumentException("Log cache directory is required.", nameof(directoryPath));
			string fullPath = Path.GetFullPath(directoryPath);
			_directoryPath = fullPath.Length > Path.GetPathRoot(fullPath).Length
				? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullPath;
			_owner = $"snipe-log-{Guid.NewGuid():N}-";
			_limits = limits ?? SnipeLogBufferLimits.Default;
			_manualWriter = !startWriter;
#if UNITY_WEBGL && !UNITY_EDITOR
			_frameWriter = true;
#else
			_frameWriter = _manualWriter;
#endif
			_cache = SnipeLogCacheDirectory.Register(_directoryPath, _owner, _limits);
			try
			{
#if !UNITY_WEBGL || UNITY_EDITOR
				if (!_frameWriter)
				{
					_writerLoop = Task.Run(async () =>
					{
						if (writerStartGate != null) await writerStartGate.ConfigureAwait(false);
						await ProcessQueueAsync().ConfigureAwait(false);
					});
				}
#endif
			}
			catch
			{
				_cache.ReleaseOwner(_owner);
				throw;
			}
		}

		// Avoid allocating serialized data when admission is already impossible.
		// Append checks again atomically when it takes ownership of the array.
		internal bool CanAccept(int bytes)
		{
			lock (_queueLock)
			{
				if (!_accepting) return false;
				if (bytes > 0 && bytes <= _limits.MaxRecordBytes && bytes <= _limits.MaxQueuedBytes - _queuedBytes)
					return true;
				_queueDrops++;
				return false;
			}
		}

		internal void NoteTruncation() { lock (_queueLock) _truncatedRecords++; }

		internal bool Append(byte[] record)
		{
			return record != null && record.Length > 0 && TryEnqueue(SnipeLogBufferCommand.Append(record));
		}

		internal async UniTask<SnipeLogCacheDirectory.SendBatch> SealCurrentFileAsync()
		{
			var command = SnipeLogBufferCommand.SealCurrentFile();
			if (!TryEnqueue(command)) return null;
			return await command.Completion.Task;
		}

		private bool TryEnqueue(SnipeLogBufferCommand command)
		{
			lock (_queueLock)
			{
				if (!_accepting) return false;
				if (command.Data != null)
				{
					if (command.Data.Length > _limits.MaxRecordBytes || command.Data.Length > _limits.MaxQueuedBytes - _queuedBytes)
					{
						_queueDrops++;
						return false;
					}
					_queuedBytes += command.Data.Length;
				}
				_commands.Enqueue(command);
#if !UNITY_WEBGL || UNITY_EDITOR
				if (!_frameWriter) _commandSignal.Release();
#endif
			}
			if (_frameWriter && !_manualWriter) SignalFrameWriter();
			return true;
		}

		private SnipeLogBufferCommand DequeueCommand()
		{
			lock (_queueLock) return _commands.Count > 0 ? _commands.Dequeue() : null;
		}

		private void ReleaseRecord(SnipeLogBufferCommand command)
		{
			if (command?.Data == null) return;
			lock (_queueLock) _queuedBytes -= command.Data.Length;
		}

#if !UNITY_WEBGL || UNITY_EDITOR
		private async Task ProcessQueueAsync()
		{
			while (true)
			{
				await _commandSignal.WaitAsync().ConfigureAwait(false);
				var command = DequeueCommand();
				if (command == null) continue;
				try
				{
					if (command.CommandType == SnipeLogBufferCommandType.Append)
					{
						if (_currentFileLength > _limits.MaxFileBytes - command.Data.Length)
							await CloseCurrentWriterAsync().ConfigureAwait(false);
						if (!PrepareRecord(command.Data.Length)) continue;
						await _currentWriter.WriteAsync(command.Data, 0, command.Data.Length).ConfigureAwait(false);
						_currentFileLength += command.Data.Length;
						_bytesSinceFlush += command.Data.Length;
						if (_bytesSinceFlush >= MIN_BYTES_TO_FLUSH)
						{
							await _currentWriter.FlushAsync().ConfigureAwait(false);
							_bytesSinceFlush = 0;
						}
						_cache.CompleteRecord(_currentFile);
					}
					else
					{
						bool closed = await CloseCurrentWriterAsync().ConfigureAwait(false);
						if (command.CommandType == SnipeLogBufferCommandType.SealCurrentFile)
							command.Completion.TrySetResult(closed ? _cache.AcquireClosedFiles(_owner) : null);
						if (command.CommandType == SnipeLogBufferCommandType.Stop) return;
					}
				}
				catch (Exception)
				{
					DiscardFailedWriter();
					command.Completion?.TrySetResult(null);
					if (command.CommandType == SnipeLogBufferCommandType.Stop) return;
				}
				finally { ReleaseRecord(command); }
			}
		}

		private async Task<bool> CloseCurrentWriterAsync()
		{
			if (_currentWriter == null) return true;
			try
			{
				await _currentWriter.FlushAsync().ConfigureAwait(false);
				_currentWriter.Dispose();
				PublishClosedFile();
				return true;
			}
			catch (Exception)
			{
				DiscardFailedWriter();
				return false;
			}
		}
#endif

		private void SignalFrameWriter()
		{
			lock (_queueLock)
			{
				if (_pumpRunning || _disposed) return;
				_pumpRunning = true;
			}
			ProcessFrameQueueAsync().Forget();
		}

		private async UniTaskVoid ProcessFrameQueueAsync()
		{
			await UniTask.Yield(PlayerLoopTiming.Update);
			while (true)
			{
				lock (_queueLock)
				{
					if (_disposed || (_frameActiveCommand == null && _commands.Count == 0))
					{
						_pumpRunning = false;
						return;
					}
				}
				ProcessFrame(WEB_GL_BYTES_PER_FRAME, WEB_GL_COMMANDS_PER_FRAME);
				await UniTask.Yield(PlayerLoopTiming.Update);
			}
		}

		// The production WebGL pump and deterministic Editor tests execute this same writer.
		internal void ProcessFrame(int maxBytes, int maxCommands)
		{
			if (!_frameWriter || maxBytes <= 0 || maxCommands <= 0) return;
			while (maxBytes > 0 && maxCommands-- > 0)
			{
				lock (_queueLock)
				{
					if (_disposed) return;
					if (_frameActiveCommand == null && _commands.Count > 0) _frameActiveCommand = _commands.Dequeue();
				}
				var command = _frameActiveCommand;
				if (command == null) return;
				bool complete = true;
				try
				{
					if (command.CommandType == SnipeLogBufferCommandType.Append)
					{
						if (command.Offset == 0)
						{
							if (_currentFileLength > _limits.MaxFileBytes - command.Data.Length) CloseCurrentWriter();
							if (!PrepareRecord(command.Data.Length)) continue;
						}
						int count = Math.Min(maxBytes, command.Data.Length - command.Offset);
						// A failed write may have written a prefix before throwing. Charge the
						// attempted chunk first so even that frame cannot exceed its budget.
						maxBytes -= count;
						_currentWriter.Write(command.Data, command.Offset, count);
						command.Offset += count;
						_currentFileLength += count;
						_bytesSinceFlush += count;
						if (_bytesSinceFlush >= MIN_BYTES_TO_FLUSH)
						{
							_currentWriter.Flush();
							_bytesSinceFlush = 0;
						}
						complete = command.Offset == command.Data.Length;
						if (complete) _cache.CompleteRecord(_currentFile);
					}
					else
					{
						bool closed = CloseCurrentWriter();
						if (command.CommandType == SnipeLogBufferCommandType.SealCurrentFile)
							command.Completion.TrySetResult(closed ? _cache.AcquireClosedFiles(_owner) : null);
					}
				}
				catch (Exception)
				{
					DiscardFailedWriter();
					command.Completion?.TrySetResult(null);
				}
				finally
				{
					if (complete)
					{
						ReleaseRecord(command);
						_frameActiveCommand = null;
					}
				}
			}
		}

		private bool PrepareRecord(int bytes)
		{
			string path = _currentFile?.Path ?? Path.Combine(_directoryPath, $"{_owner}{_fileSequence++:D8}.ndjson");
			bool reserved = _cache.TryReserve(_owner, path, _currentFile, bytes, out var file, out long evicted);
			lock (_queueLock)
			{
				_evictedRecords += evicted;
				if (!reserved) _quotaDrops++;
			}
			if (!reserved) return false;
			_currentFile = file;
			if (_currentWriter == null)
			{
				Directory.CreateDirectory(_directoryPath);
				_currentWriter = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, !_frameWriter);
			}
			return true;
		}

		private bool CloseCurrentWriter()
		{
			if (_currentWriter == null) return true;
			try
			{
				_currentWriter.Flush();
				_currentWriter.Dispose();
				PublishClosedFile();
				return true;
			}
			catch (Exception)
			{
				DiscardFailedWriter();
				return false;
			}
		}

		private void PublishClosedFile()
		{
			_cache.Seal(_currentFile);
			_currentWriter = null;
			_currentFile = null;
			_currentFileLength = 0;
			_bytesSinceFlush = 0;
		}

		private void DiscardFailedWriter()
		{
			try { _currentWriter?.Dispose(); }
			catch (Exception) { /* Retain the full reservation if deletion also fails. */ }
			_cache.DiscardFailedFile(_currentFile);
			_currentWriter = null;
			_currentFile = null;
			_currentFileLength = 0;
			_bytesSinceFlush = 0;
			lock (_queueLock) _writeFailures++;
		}

		internal void EmitDiagnostics()
		{
			string message;
			lock (_queueLock)
			{
				if (_truncatedRecords == 0 && _queueDrops == 0 && _quotaDrops == 0 && _evictedRecords == 0 && _writeFailures == 0) return;
				message = $"{SnipeLogPipeline.DIAGNOSTIC_LOG_PREFIX} Buffered logs: truncated={_truncatedRecords}, queueDropped={_queueDrops}, cacheDropped={_quotaDrops}, evicted={_evictedRecords}, writeFailures={_writeFailures}.";
				_truncatedRecords = _queueDrops = _quotaDrops = _evictedRecords = _writeFailures = 0;
			}
			DebugLogger.LogWarning(message);
		}

		public void Dispose()
		{
			SnipeLogBufferCommand[] pending;
			SnipeLogBufferCommand active;
			lock (_queueLock)
			{
				if (_disposed) return;
				_disposed = true;
				_accepting = false;
				pending = _commands.ToArray();
				_commands.Clear();
				active = _frameActiveCommand;
				_frameActiveCommand = null;
#if !UNITY_WEBGL || UNITY_EDITOR
				if (!_frameWriter)
				{
					_commands.Enqueue(SnipeLogBufferCommand.Stop());
					_commandSignal.Release();
				}
#endif
			}
			foreach (var command in pending)
			{
				ReleaseRecord(command);
				command.Completion?.TrySetResult(null);
			}
			ReleaseRecord(active);
			active?.Completion?.TrySetResult(null);
			try
			{
				if (_frameWriter) CloseCurrentWriter();
#if !UNITY_WEBGL || UNITY_EDITOR
				else _writerLoop.GetAwaiter().GetResult();
#endif
			}
			finally
			{
#if !UNITY_WEBGL || UNITY_EDITOR
				_commandSignal.Dispose();
#endif
				_cache.ReleaseOwner(_owner);
				EmitDiagnostics();
			}
		}
	}
}
