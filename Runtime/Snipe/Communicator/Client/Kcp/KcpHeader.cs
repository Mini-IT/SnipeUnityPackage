namespace MiniIT.Snipe
{
	public enum KcpHeader : byte
	{
		None = 0, // don't react on 0x00. might help to filter out random noise.
		Handshake = 1,
		Ping = 0x02, // reliable
		Data = 0x03,
		Disconnect = 0x04,
		Chunk = 0x05, // fragment of a large data packet splitted into several messages
		CompressedChunk = 0x06,
	}
}