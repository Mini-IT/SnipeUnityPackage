using System;

namespace MiniIT.Snipe
{
	public interface IStopwatch
	{
		public TimeSpan Elapsed { get; }
		public bool IsRunning { get; }

		/// <summary>
		/// Stops time interval measurement and resets the elapsed time to zero.
		/// </summary>
		public void Reset();

		/// <summary>
		/// Stops time interval measurement, resets the elapsed time to zero, and starts measuring elapsed time.
		/// </summary>
		public void Restart();

		public void Start();
		public void Stop();
	}

	public interface IStopwatchFactory
	{
		public IStopwatch Create();
	}
}
