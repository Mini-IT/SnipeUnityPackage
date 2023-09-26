
namespace MiniIT.Snipe.Unity
{
	public class DeviceIdBinding : AuthBinding<DeviceIdFetcher>
	{
		public DeviceIdBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base("dvid", communicator, authSubsystem, config)
		{
		}
	}
}
