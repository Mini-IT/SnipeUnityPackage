namespace MiniIT.Snipe
{
	public interface ISnipeAnalyticsService
	{
		bool IsEnabled { get; set; }
		ISnipeAnalyticsTracker GetTracker(int contextId = 0);
		void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker);
	}
}
