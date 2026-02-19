namespace MiniIT.Snipe
{
	public interface ISnipeConfigLoadingAnalyticsTracker
	{
		void TrackSnipeConfigLoadingStats(SnipeConfigLoadingStatistics statistics);
	}
}
