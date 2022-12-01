using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class Transport : IDisposable
	{
		public Action ConnectionOpenedHandler;
		public Action ConnectionClosedHandler;
		public Action<SnipeObject> MessageReceivedHandler;
		
		protected SnipeMessageCompressor _messageCompressor = new SnipeMessageCompressor();
		protected byte[] _messageSerializationBuffer = new byte[10240];
		protected SemaphoreSlim _messageSerializationSemaphore;

		protected readonly ConcurrentQueue<Action> _mainThreadActions;
		protected CancellationTokenSource _mainThreadLoopCancellation;

		public Transport()
		{
			_messageSerializationSemaphore = new SemaphoreSlim(1);

			_mainThreadActions = new ConcurrentQueue<Action>();
			_mainThreadLoopCancellation = new CancellationTokenSource();
			MainThreadLoop(_mainThreadLoopCancellation.Token);
		}

		~Transport()
		{
			Dispose();
		}

		private async void MainThreadLoop(CancellationToken cancellationToken)
		{
			while (cancellationToken != null && !cancellationToken.IsCancellationRequested)
			{
				if (_mainThreadActions != null && _mainThreadActions.TryDequeue(out var action))
				{
					action?.Invoke();
				}

				await Task.Delay(50);
			}
		}

		public virtual void Dispose()
		{
			if (_mainThreadLoopCancellation != null)
			{
				_mainThreadLoopCancellation.Cancel();
				_mainThreadLoopCancellation = null;
			}
		}
	}
}