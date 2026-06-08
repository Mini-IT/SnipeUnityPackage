using System;

namespace MiniIT.Snipe
{
	internal sealed class SnipeRequestRateWindow
	{
		private const int RATE_LIMIT_INTERVAL_MS = 1000;

		private readonly Func<long> _getTimestamp;
		private readonly long _timestampFrequency;

		private long _windowStartTimestamp;
		private int _requestsSentInWindow;

		public SnipeRequestRateWindow(Func<long> getTimestamp, long timestampFrequency)
		{
			_getTimestamp = getTimestamp;
			_timestampFrequency = timestampFrequency;
		}

		public int GetAvailableSlots(int requestsPerSecondLimit, out int delayMs)
		{
			long now = _getTimestamp();

			if (_windowStartTimestamp == 0)
			{
				_windowStartTimestamp = now;
				_requestsSentInWindow = 0;
			}

			double elapsedMs = (now - _windowStartTimestamp) * 1000d / _timestampFrequency;

			if (elapsedMs >= RATE_LIMIT_INTERVAL_MS)
			{
				_windowStartTimestamp = now;
				_requestsSentInWindow = 0;
				elapsedMs = 0;
			}

			delayMs = Math.Max(1, RATE_LIMIT_INTERVAL_MS - (int)elapsedMs);
			return Math.Max(0, requestsPerSecondLimit - _requestsSentInWindow);
		}

		public void Reserve(int requestCount)
		{
			_requestsSentInWindow += Math.Max(1, requestCount);
		}

		public void Release(int requestCount)
		{
			_requestsSentInWindow = Math.Max(0, _requestsSentInWindow - Math.Max(1, requestCount));
		}

		public void Clear()
		{
			_windowStartTimestamp = 0;
			_requestsSentInWindow = 0;
		}
	}
}
