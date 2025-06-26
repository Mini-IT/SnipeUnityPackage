using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MiniIT.Snipe
{
	public class SnipeConfigLoadingStatistics
	{
		public enum LoadingState
		{
			NotStarted,
			Initialization,
			Loading,
			Finished,
			Cancelled,
		}

		public struct StateChange
		{
			public LoadingState State;
			public TimeSpan Time;
		}

		/// <summary>
		/// Filled externally by the tracker
		/// </summary>
		public int FireCount { get; set; }

		public string PackageVersionName { get; } = PackageInfo.VERSION_NAME;
		public List<StateChange> StateChanges { get; } = new List<StateChange>(4);
		public LoadingState State { get; private set; } = LoadingState.NotStarted;
		public bool Success { get; set; } = false;
		public TimeSpan CurrentStateTime { get; private set; }
		public string ClientImplementation { get; set; }

		private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

		public void SetState(LoadingState state)
		{
			var time = _stopwatch.Elapsed;

			State = state;
			CurrentStateTime = time;

			StateChanges.Add(new StateChange
			{
				State = state,
				Time = time,
			});
		}
	}
}
