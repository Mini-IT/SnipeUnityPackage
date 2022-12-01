using System;
using System.Threading;
using System.Threading.Tasks;

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

		private TaskScheduler _mainThreadScheduler;

		public Transport()
		{
			_messageSerializationSemaphore = new SemaphoreSlim(1);

			_mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
		}

		protected void RunInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}
	}
}