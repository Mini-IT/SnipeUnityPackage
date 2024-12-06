using System;
using System.Diagnostics;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class UniTimer
	{
		public bool IsRunning => (_prevTickTimestamp >= 0);

		private readonly TimeSpan _interval;
		private readonly Action _onTick;

		private long _prevTickTimestamp = -1;

		public UniTimer(TimeSpan interval, Action onTick)
		{
			_interval = interval;
			_onTick = onTick;
		}

		public void Start()
		{
			_prevTickTimestamp = Stopwatch.GetTimestamp();
			Run().Forget();
		}

		public void Stop()
		{
			_prevTickTimestamp = -1;
		}

		private async UniTaskVoid Run()
		{
			await UniTask.Delay(_interval);

			while (IsRunning)
			{
				long passedTicks = Stopwatch.GetTimestamp() - _prevTickTimestamp;
				long passedIntervals = passedTicks / _interval.Ticks;
				_prevTickTimestamp += passedTicks * _interval.Ticks;

				for (int i = 0; i < passedIntervals; i++)
				{
					_onTick?.Invoke();
				}

				await UniTask.Delay(_interval);
			}
		}
	}
}
