using System;

namespace MiniIT.Snipe.Internal
{
	internal sealed class SnipeLogBufferLimits
	{
		internal static SnipeLogBufferLimits Default { get; } = new SnipeLogBufferLimits();

		internal int MaxRecordBytes { get; }
		internal int MaxQueuedBytes { get; }
		internal int MaxFileBytes { get; }
		internal long MaxCacheBytes { get; }
		internal int MaxCacheFiles { get; }

		internal SnipeLogBufferLimits(int maxRecordBytes = 256 * 1024, int maxQueuedBytes = 1024 * 1024,
			int maxFileBytes = 1024 * 1024, long maxCacheBytes = 8 * 1024 * 1024, int maxCacheFiles = 64)
		{
			if (maxRecordBytes <= 0 || maxRecordBytes > maxQueuedBytes || maxRecordBytes > maxFileBytes ||
				maxFileBytes > maxCacheBytes || maxCacheFiles <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxRecordBytes), "Log buffer limits must be positive and fit within their enclosing budgets.");
			}

			MaxRecordBytes = maxRecordBytes;
			MaxQueuedBytes = maxQueuedBytes;
			MaxFileBytes = maxFileBytes;
			MaxCacheBytes = maxCacheBytes;
			MaxCacheFiles = maxCacheFiles;
		}

		internal bool HasSameDiskLimits(SnipeLogBufferLimits other)
		{
			return MaxFileBytes == other.MaxFileBytes && MaxCacheBytes == other.MaxCacheBytes && MaxCacheFiles == other.MaxCacheFiles;
		}
	}
}
