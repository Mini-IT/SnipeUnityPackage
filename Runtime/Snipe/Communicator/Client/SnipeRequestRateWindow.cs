using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	internal sealed class SnipeRequestRateWindow
	{
		private const int RATE_LIMIT_INTERVAL_MS = 1000;

		private readonly Func<long> _getTimestamp;
		private readonly long _timestampFrequency;

		private readonly Queue<long> _sentTimestamps = new Queue<long>();
		private int _reservedSlots;

		public SnipeRequestRateWindow(Func<long> getTimestamp, long timestampFrequency)
		{
			_getTimestamp = getTimestamp;
			_timestampFrequency = timestampFrequency;
		}

		public int GetAvailableSlots(int requestsPerSecondLimit, out int delayMs)
		{
			long now = _getTimestamp();

			while (_sentTimestamps.Count > 0 && now - _sentTimestamps.Peek() >= _timestampFrequency)
			{
				_sentTimestamps.Dequeue();
			}

			delayMs = 1;
			if (_sentTimestamps.Count > 0)
			{
				long remaining = _timestampFrequency - (now - _sentTimestamps.Peek());
				delayMs = Math.Max(1, (int)Math.Ceiling(remaining * (double)RATE_LIMIT_INTERVAL_MS / _timestampFrequency));
			}

			return Math.Max(0, requestsPerSecondLimit - _sentTimestamps.Count - _reservedSlots);
		}

		public void Reserve(int requestCount)
		{
			_reservedSlots += requestCount;
		}

		public void Release(int requestCount)
		{
			_reservedSlots -= requestCount;
		}

		public void RecordSent(int requestCount)
		{
			long now = _getTimestamp();
			for (int i = 0; i < requestCount; i++)
			{
				_sentTimestamps.Enqueue(now);
			}
		}

		public void ClearReservations()
		{
			_reservedSlots = 0;
		}
	}
}
