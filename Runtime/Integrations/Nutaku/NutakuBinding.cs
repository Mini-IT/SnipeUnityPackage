namespace MiniIT.Snipe.Unity
{
#if UNITY_WEBGL
	using NutakuIdFetcher = WebGLNutakuIdFetcher;
#elif UNITY_ANDROID
	using NutakuIdFetcher = AndroidNutakuIdFetcher;
#endif

	public class NutakuBinding : AuthBinding<NutakuIdFetcher>
	{
        public NutakuBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
            : base("nuta", communicator, authSubsystem, config) { }
	}
}
