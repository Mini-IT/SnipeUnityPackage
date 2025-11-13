namespace MiniIT.Snipe.Unity
{
    public sealed class WebGLBinding : AuthBinding<WebGLIdFetcher>
    {
        public WebGLBinding(SnipeCommunicator communicator, AuthSubsystem authSubsystem, SnipeConfig config)
            : base("dvid", communicator, authSubsystem, config) { }
    }
}