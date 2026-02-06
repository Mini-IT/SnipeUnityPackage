using System;
using System.Collections.Generic;
using MiniIT.Snipe.Debugging;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	internal class SnipeAnalyticsService : ISnipeAnalyticsService, IAnalyticsTrackerProvider
	{
		public bool Enabled { get; set; } = true;

		private ISnipeCommunicatorAnalyticsTracker _externalTracker;
		private readonly Func<ISnipeErrorsTracker> _errorsTrackerGetter;
		private readonly IMainThreadRunner _mainThreadRunner;

		private Dictionary<int, AnalyticsContext> _trackers;
		private readonly object _trackersLock = new object();

		public SnipeAnalyticsService(IMainThreadRunner mainThreadRunner, Func<ISnipeErrorsTracker> errorsTrackerGetter = null)
		{
			_mainThreadRunner = mainThreadRunner;
			_errorsTrackerGetter = errorsTrackerGetter;
		}

		public IAnalyticsContext GetTracker(int contextId = 0)
		{
			AnalyticsContext context;

			lock (_trackersLock)
			{
				_trackers ??= new Dictionary<int, AnalyticsContext>(1);
				if (!_trackers.TryGetValue(contextId, out context))
				{
					context = new AnalyticsContext(this, contextId, _errorsTrackerGetter, _mainThreadRunner);
					_trackers[contextId] = context;
					context.SetExternalTracker(_externalTracker);
				}
			}
			return context;
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
