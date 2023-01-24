using MiniIT.MessagePack;
using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public class Transport
	{
		public Action ConnectionOpenedHandler;
		public Action ConnectionClosedHandler;
		public Action<SnipeObject> MessageReceivedHandler;
		
		protected readonly SnipeMessageCompressor _messageCompressor = new SnipeMessageCompressor();
		protected readonly MessagePackSerializerNonAlloc _messageSerializer = new MessagePackSerializerNonAlloc();
		protected byte[] _messageSerializationBuffer = new byte[10240];

		protected readonly SemaphoreSlim _messageSerializationSemaphore = new SemaphoreSlim(1, 1);
		protected readonly SemaphoreSlim _messageProcessingSemaphore = new SemaphoreSlim(1, 1);
	}
}