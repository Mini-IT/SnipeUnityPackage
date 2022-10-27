using System;

namespace MiniIT.Snipe
{
	public class SnipeConnection
	{
		public Action ConnectionOpenedHandler;
		public Action ConnectionClosedHandler;
		public Action<SnipeObject> MessageReceivedHandler;
		
		protected SnipeMessageCompressor mMessageCompressor = new SnipeMessageCompressor();
		protected MessageBufferProvider mMessageBufferProvider = new MessageBufferProvider();
	}
}