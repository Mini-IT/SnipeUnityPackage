namespace MiniIT.Snipe
{
	public enum KcpOpCode : byte
	{
		None = 0,
		Heartbeat = 200,
		AuthenticationRequest = 0x01,
		AuthenticationResponse = 0x02,
		Authenticated = 0x03,
		SnipeRequest = 0x04,
		SnipeResponse = 0x05,
		SnipeRequestCompressed = 0x06,
		SnipeResponseCompressed = 0x07,
	}
}
