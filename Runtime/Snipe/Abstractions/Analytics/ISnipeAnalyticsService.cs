namespace MiniIT.Snipe
{
	public interface ISnipeAnalyticsService
	{
		bool Enabled { get; set; }
		void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker);
	}
}
