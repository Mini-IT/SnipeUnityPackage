
namespace MiniIT.Snipe
{
	public class DeviceIdBinding : AuthBinding<DeviceIdFetcher>
	{
		public DeviceIdBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base("dvid", communicator, authSubsystem, config)
		{
		}
	}
}
