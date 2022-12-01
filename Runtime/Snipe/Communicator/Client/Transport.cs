using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public class Transport
	{
		public Action ConnectionOpenedHandler;
		public Action ConnectionClosedHandler;
		public Action<SnipeObject> MessageReceivedHandler;
		
		protected SnipeMessageCompressor _messageCompressor = new SnipeMessageCompressor();
		protected byte[] _messageSerializationBuffer = new byte[10240];
		protected SemaphoreSlim _messageSerializationSemaphore;

		public Transport()
		{
			_messageSerializationSemaphore = new SemaphoreSlim(1);
		}
	}
}