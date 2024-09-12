#if UNITY_WEBGL
#define FORCE_UNITASK
#endif

#if FORCE_UNITASK

using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniIT.Threading;

namespace MiniIT.Threading
{
	public class AlterSemaphore : IDisposable
	{
		public int CurrentCount => _count;

		private static readonly object s_lock = new object();

		private int _count;
		private readonly int _maxCount;

		public AlterSemaphore(int initialCount)
		{
			_count = initialCount;
			_maxCount = 0;
		}

		public AlterSemaphore(int initialCount, int maxCount)
		{
			_count = initialCount;
			_maxCount = maxCount;
		}
		public int Release() => Release(1);

		public int Release(int releaseCount)
		{
			if (releaseCount < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(releaseCount));
			}

			lock (s_lock)
			{
				if (_count < 0)
				{
					throw new ObjectDisposedException(this.ToString());
				}

				int count = _count + releaseCount;

				if (_maxCount > 0 && count > _maxCount)
				{
					throw new SemaphoreFullException();
				}

				int prevCount = _count;
				_count = count;

				return prevCount;
			}
		}

		public UniTask WaitAsync()
			=> WaitAsync(CancellationToken.None);
		public UniTask<bool> WaitAsync(int millisecondsTimeout)
			=> WaitAsync(TimeSpan.FromMilliseconds(millisecondsTimeout));
		public UniTask<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken)
			=> WaitAsync(TimeSpan.FromMilliseconds(millisecondsTimeout), cancellationToken);

		public async UniTask WaitAsync(CancellationToken cancellationToken)
		{
			while (true)
			{
				lock (s_lock)
				{
					if (_count > 0)
					{
						_count--;
						break;
					}
				}

				await TaskHelper.Yield();

				if (_count < 0)
				{
					throw new ObjectDisposedException(this.ToString());
				}

				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		public UniTask<bool> WaitAsync(TimeSpan timeout)
			=> WaitAsync(timeout, CancellationToken.None);

		public async UniTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
		{
			var stopwatch = Stopwatch.StartNew();

			while (true)
			{
				lock (s_lock)
				{
					if (_count > 0)
					{
						_count--;
						return true;
					}
				}

				await TaskHelper.Yield();

				if (_count < 0)
				{
					throw new ObjectDisposedException(this.ToString());
				}

				cancellationToken.ThrowIfCancellationRequested();

				if (stopwatch.Elapsed >= timeout)
				{
					break;
				}
			}

			return false;
		}

		public void Dispose()
		{
			_count = -1;
		}
	}
}

#endif
