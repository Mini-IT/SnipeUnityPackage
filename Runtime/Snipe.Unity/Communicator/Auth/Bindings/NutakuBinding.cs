#if NUTAKU
namespace MiniIT.Snipe.Unity
{
	public class NutakuBinding : AuthBinding<NutakuIdFetcher>
	{
		public NutakuBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
			: base("dvid", communicator, authSubsystem, config)
		{
		}
	}
}
#endif
