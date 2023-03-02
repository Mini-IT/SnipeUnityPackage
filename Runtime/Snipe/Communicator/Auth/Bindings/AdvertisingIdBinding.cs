
namespace MiniIT.Snipe
{
	public class AdvertisingIdBinding : AuthBinding<AdvertisingIdFetcher>
	{
		public AdvertisingIdBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem)
			: base("adid", communicator, authSubsystem)
		{
		}
	}
}
