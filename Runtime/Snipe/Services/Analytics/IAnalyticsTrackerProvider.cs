namespace MiniIT.Snipe
{
	public interface IAnalyticsTrackerProvider
	{
		IAnalyticsContext GetTracker(int contextId = 0);
	}
}
