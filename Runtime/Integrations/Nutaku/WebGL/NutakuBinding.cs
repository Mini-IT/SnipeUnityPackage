#if NUTAKU_WEB
namespace MiniIT.Snipe.Unity
{
	public class NutakuBinding : AuthBinding<NutakuWebIdFetcher>
	{
        public NutakuBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
            : base("nuta", communicator, authSubsystem, config) { }
	}
}
#endif
