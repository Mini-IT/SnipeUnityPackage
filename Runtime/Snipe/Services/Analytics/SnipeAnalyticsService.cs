using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeAnalyticsService : ISnipeAnalyticsService
	{
		public bool IsEnabled { get; set; } = true;

		private ISnipeCommunicatorAnalyticsTracker _externalTracker;

		private Dictionary<string, SnipeAnalyticsTracker> _trackers;
		private readonly object _trackersLock = new object();

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

		public void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker)
		{
			_externalTracker = externalTracker;

			lock (_trackersLock)
			{
				if (_trackers == null)
				{
					return;
				}

				foreach (var tracker in _trackers.Values)
				{
					tracker?.SetExternalTracker(externalTracker);
				}
			}
		}
	}
}
