using MiniIT.Unity;

namespace MiniIT.Snipe.Unity
{
	public class FuzzyStopwatch : FrameRateDrivenStopwatch, IStopwatch
	{
	}

	public class FuzzyStopwatchFactory : IStopwatchFactory
	{
		public IStopwatch Create() => new FuzzyStopwatch();
	}
}
