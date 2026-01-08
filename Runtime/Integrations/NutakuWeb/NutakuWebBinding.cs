#if NUTAKU_WEB
namespace MiniIT.Snipe.Unity
{
	public class NutakuWebBinding : AuthBinding<NutakuWebIdFetcher>
	{
        public NutakuWebBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
            : base("nuta", communicator, authSubsystem, config) { }
	}
}
#endif
