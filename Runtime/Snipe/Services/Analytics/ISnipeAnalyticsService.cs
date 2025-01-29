namespace MiniIT.Snipe
{
	public interface ISnipeAnalyticsService
	{
		bool IsEnabled { get; set; }
		SnipeAnalyticsTracker GetTracker(int contextId = 0);
		void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker);
	}
}
