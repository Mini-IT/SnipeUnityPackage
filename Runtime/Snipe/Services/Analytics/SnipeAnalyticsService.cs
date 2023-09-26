using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeAnalyticsService : IDisposable
	{
		public bool IsEnabled { get; set; } = true;

		public SnipeAnalyticsTracker GetTracker(string contextId = null)
		{
			contextId ??= string.Empty;
			SnipeAnalyticsTracker tracker;
			lock (_trackersLock)
			{
				_trackers ??= new Dictionary<string, SnipeAnalyticsTracker>();
				if (!_trackers.TryGetValue(contextId, out tracker))
				{
					tracker = new SnipeAnalyticsTracker(this, contextId);
					_trackers[contextId] = tracker;
					tracker.SetExternalTracker(_externalTracker);
				}
			}
			return tracker;
		}

		private ISnipeCommunicatorAnalyticsTracker _externalTracker;

		private Dictionary<string, SnipeAnalyticsTracker> _trackers;
		private readonly object _trackersLock = new object();

		public void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker)
		{
			_externalTracker = externalTracker;
			lock (_trackersLock)
			{
				foreach (var tracker in _trackers.Values)
				{
					tracker?.SetExternalTracker(externalTracker);
				}
			}
		}

		~SnipeAnalyticsService() => Dispose();

		public void Dispose()
		{
			if (_trackers != null)
			{
				// Local copy for thread safety
				var trackers = _trackers;
				foreach (var pair in trackers)
				{
					pair.Value.Dispose();
				}
			}

			GC.SuppressFinalize(this);
		}

		internal void RemoveTracker(SnipeAnalyticsTracker tracker)
		{
			lock (_trackersLock)
			{
				foreach (var pair in _trackers)
				{
					if (pair.Value == tracker)
					{
						_trackers.Remove(pair.Key);
						break;
					}
				}
			}
		}
	}
}
