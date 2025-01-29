using MiniIT.Unity;

namespace MiniIT.Snipe.Unity
{
	public class FuzzyStopwatch : FrameRateDrivenStopwatch, IStopwatch
	{
	}

	public class FuzzyStopwatchFactory : IStopwatchFactory
	{
		public virtual IStopwatch Create() => new FuzzyStopwatch();
	}
}
