namespace MiniIT.Snipe
{
	public enum TransportProtocol
	{
		Undefined,
		Kcp,
		WebSocket,
		Http,
	}

	public struct TransportInfo
	{
		public TransportProtocol Protocol;
		public string ClientImplementation;
	}
}
