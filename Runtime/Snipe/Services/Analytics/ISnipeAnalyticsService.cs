namespace MiniIT.Snipe
{
	public interface ISnipeAnalyticsService
	{
		bool IsEnabled { get; set; }
		SnipeAnalyticsTracker GetTracker(string contextId = null);
		void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker);
	}
}
