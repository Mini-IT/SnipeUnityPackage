using MiniIT.Snipe.Debugging;

namespace MiniIT.Snipe
{
	public interface ISnipeErrorsTrackerProvider
	{
		ISnipeErrorsTracker ErrorsTracker { get; }
	}
}
