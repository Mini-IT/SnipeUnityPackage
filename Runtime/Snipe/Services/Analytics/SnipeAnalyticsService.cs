using System;
using System.Collections.Generic;
using MiniIT.Snipe.Debugging;

namespace MiniIT.Snipe
{
	public class SnipeAnalyticsService : ISnipeAnalyticsService
	{
		public bool IsEnabled { get; set; } = true;

		private ISnipeCommunicatorAnalyticsTracker _externalTracker;
		private readonly Func<ISnipeErrorsTracker> _errorsTrackerGetter;

		private Dictionary<int, SnipeAnalyticsTracker> _trackers;
		private readonly object _trackersLock = new object();

		public SnipeAnalyticsService(Func<ISnipeErrorsTracker> errorsTrackerGetter = null)
		{
			_errorsTrackerGetter = errorsTrackerGetter;
		}

		public SnipeAnalyticsTracker GetTracker(int contextId = 0)
		{
			SnipeAnalyticsTracker tracker;

			lock (_trackersLock)
			{
				_trackers ??= new Dictionary<int, SnipeAnalyticsTracker>(1);
				if (!_trackers.TryGetValue(contextId, out tracker))
				{
					tracker = new SnipeAnalyticsTracker(this, contextId, _errorsTrackerGetter);
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
