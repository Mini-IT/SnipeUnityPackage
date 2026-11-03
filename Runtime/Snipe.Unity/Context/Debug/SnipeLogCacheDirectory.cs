using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MiniIT.Snipe.Internal
{
	internal sealed class SnipeLogCacheDirectory
	{
		private const string FILE_PATTERN = "snipe-log-*.ndjson";
		private static readonly object s_registryLock = new object();
		private static readonly StringComparer s_pathComparer =
			Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		private static readonly Dictionary<string, SnipeLogCacheDirectory> s_directories =
			new Dictionary<string, SnipeLogCacheDirectory>(s_pathComparer);

		private readonly object _syncRoot = new object();
		private readonly string _path;
		private readonly SnipeLogBufferLimits _limits;
		private readonly HashSet<string> _owners = new HashSet<string>(StringComparer.Ordinal);
		private readonly Dictionary<string, FileEntry> _files = new Dictionary<string, FileEntry>(s_pathComparer);
		private int _references;
		private long _usedBytes;
		private long _orphanBytes;
		private long _orphanFiles;
		private bool _unknownOrphanSize;
		private long _sequence;
		private long _reservationAttempt;

		private SnipeLogCacheDirectory(string path, SnipeLogBufferLimits limits)
		{
			_path = path;
			_limits = limits;
		}

		internal static SnipeLogCacheDirectory Register(string path, string owner, SnipeLogBufferLimits limits)
		{
			SnipeLogCacheDirectory directory;
			lock (s_registryLock)
			{
				if (!s_directories.TryGetValue(path, out directory))
				{
					directory = new SnipeLogCacheDirectory(path, limits);
					s_directories.Add(path, directory);
				}
				else if (!directory._limits.HasSameDiskLimits(limits))
				{
					throw new ArgumentException("Log buffers sharing a cache directory must use the same disk limits.", nameof(limits));
				}
				Interlocked.Increment(ref directory._references);
			}

			try
			{
				lock (directory._syncRoot)
				{
					directory._owners.Add(owner);
					directory.CleanupOrphansUnsafe();
				}
				return directory;
			}
			catch
			{
				directory.ReleaseOwner(owner);
				throw;
			}
		}

		internal long UsedBytes
		{
			get { lock (_syncRoot) return _orphanBytes > long.MaxValue - _usedBytes ? long.MaxValue : _usedBytes + _orphanBytes; }
		}

		internal long FileCount
		{
			get { lock (_syncRoot) return _orphanFiles + _files.Count; }
		}

		internal bool TryReserve(string owner, string newPath, FileEntry current, int bytes,
			out FileEntry file, out long evictedRecords)
		{
			lock (_syncRoot)
			{
				file = null;
				evictedRecords = 0;
				if (!_owners.Contains(owner) || bytes <= 0 || bytes > _limits.MaxFileBytes)
				{
					return false;
				}

				bool create = current == null;
				if (!create && (current.Owner != owner || current.Sealed || current.Faulted || current.Deleted ||
					current.ChargedBytes > _limits.MaxFileBytes - bytes))
				{
					return false;
				}

				long attempt = ++_reservationAttempt;
				while (!FitsUnsafe(bytes, create ? 1 : 0))
				{
					FileEntry oldest = null;
					foreach (var candidate in _files.Values)
					{
						if (candidate.Owner == owner && candidate.Sealed && candidate.Pins == 0 &&
							candidate.LastDeleteAttempt != attempt && (oldest == null || candidate.Sequence < oldest.Sequence))
						{
							oldest = candidate;
						}
					}
					if (oldest == null) return false;
					oldest.LastDeleteAttempt = attempt;
					if (DeleteUnsafe(oldest))
					{
						evictedRecords += oldest.RecordCount - oldest.AcknowledgedRecords;
					}
				}

				file = current;
				if (create)
				{
					file = new FileEntry(owner, newPath, _sequence++);
					_files.Add(newPath, file);
				}
				file.ChargedBytes += bytes;
				_usedBytes += bytes;
				return true;
			}
		}

		private bool FitsUnsafe(int bytes, int files)
		{
			return !_unknownOrphanSize && _orphanBytes <= _limits.MaxCacheBytes &&
				_usedBytes <= _limits.MaxCacheBytes - _orphanBytes - bytes &&
				_orphanFiles + _files.Count + files <= _limits.MaxCacheFiles;
		}

		internal void CompleteRecord(FileEntry file)
		{
			lock (_syncRoot) file.RecordCount++;
		}

		internal void Seal(FileEntry file)
		{
			if (file == null) return;
			lock (_syncRoot) file.Sealed = true;
		}

		internal void DiscardFailedFile(FileEntry file)
		{
			if (file == null) return;
			lock (_syncRoot)
			{
				file.Faulted = true;
				file.Sealed = true;
				DeleteUnsafe(file);
			}
		}

		internal SendBatch AcquireClosedFiles(string owner)
		{
			lock (_syncRoot)
			{
				var files = new List<FileEntry>();
				foreach (var file in _files.Values)
				{
					if (file.Owner == owner && file.Sealed && !file.Faulted)
					{
						files.Add(file);
						file.Pins++;
					}
				}
				files.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
				Interlocked.Increment(ref _references);
				return new SendBatch(this, files.ToArray());
			}
		}

		internal void ReleaseOwner(string owner)
		{
			lock (_syncRoot)
			{
				_owners.Remove(owner);
				var ownedFiles = new List<FileEntry>();
				foreach (var file in _files.Values)
				{
					if (file.Owner == owner) ownedFiles.Add(file);
				}
				foreach (var file in ownedFiles)
				{
					if (file.Pins == 0) DeleteUnsafe(file);
				}
			}
			ReleaseReference();
		}

		private void ReleaseReference()
		{
			lock (s_registryLock)
			{
				if (Interlocked.Decrement(ref _references) == 0)
				{
					s_directories.Remove(_path);
				}
			}
		}

		private bool DeleteUnsafe(FileEntry file)
		{
			if (file.Deleted) return true;
			try
			{
				File.Delete(file.Path);
			}
			catch (Exception)
			{
				return false;
			}
			file.Deleted = true;
			_files.Remove(file.Path);
			_usedBytes -= file.ChargedBytes;
			return true;
		}

		private void CleanupOrphansUnsafe()
		{
			_orphanBytes = 0;
			_orphanFiles = 0;
			_unknownOrphanSize = false;
			try
			{
				if (!Directory.Exists(_path)) return;
				foreach (string path in Directory.EnumerateFiles(_path, FILE_PATTERN))
				{
					if (_files.TryGetValue(path, out var tracked))
					{
						if (!_owners.Contains(tracked.Owner) && tracked.Pins == 0) DeleteUnsafe(tracked);
						continue;
					}
					bool active = false;
					foreach (string owner in _owners)
					{
						if (Path.GetFileName(path).StartsWith(owner, StringComparison.Ordinal))
						{
							active = true;
							break;
						}
					}
					if (active) continue;
					try { File.Delete(path); }
					catch (Exception)
					{
						_orphanFiles++;
						long length = new FileInfo(path).Length;
						_orphanBytes = length > long.MaxValue - _orphanBytes ? long.MaxValue : _orphanBytes + length;
					}
				}
			}
			catch (Exception)
			{
				_unknownOrphanSize = true;
			}
		}

		internal sealed class FileEntry
		{
			internal string Owner { get; }
			internal string Path { get; }
			internal long Sequence { get; }
			internal long ChargedBytes { get; set; }
			internal long RecordCount { get; set; }
			internal long AcknowledgedRecords { get; set; }
			internal int Pins { get; set; }
			internal bool Sealed { get; set; }
			internal bool Faulted { get; set; }
			internal bool Deleted { get; set; }
			internal long LastDeleteAttempt { get; set; }

			internal FileEntry(string owner, string path, long sequence)
			{
				Owner = owner;
				Path = path;
				Sequence = sequence;
			}
		}

		internal sealed class SendBatch : IDisposable
		{
			private readonly SnipeLogCacheDirectory _directory;
			private int _disposed;
			internal FileEntry[] Files { get; }

			internal SendBatch(SnipeLogCacheDirectory directory, FileEntry[] files)
			{
				_directory = directory;
				Files = files;
			}

			internal long GetAcknowledgedRecords(FileEntry file)
			{
				lock (_directory._syncRoot) return file.AcknowledgedRecords;
			}

			internal void Acknowledge(FileEntry file, int count)
			{
				lock (_directory._syncRoot)
				{
					if (count < 0 || count > file.RecordCount - file.AcknowledgedRecords)
						throw new InvalidDataException("Invalid log acknowledgement count.");
					file.AcknowledgedRecords += count;
				}
			}

			internal bool DeleteSentFile(FileEntry file)
			{
				lock (_directory._syncRoot) return _directory.DeleteUnsafe(file);
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
				lock (_directory._syncRoot)
				{
					foreach (var file in Files)
					{
						file.Pins--;
						if (file.Pins == 0 && !_directory._owners.Contains(file.Owner)) _directory.DeleteUnsafe(file);
					}
				}
				_directory.ReleaseReference();
			}
		}
	}
}
