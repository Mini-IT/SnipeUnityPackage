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
		
		protected SnipeMessageCompressor mMessageCompressor = new SnipeMessageCompressor();
		protected MessageBufferProvider mMessageBufferProvider = new MessageBufferProvider();

		protected readonly ConcurrentQueue<Action> mMainThreadActions;
		protected CancellationTokenSource mMainThreadLoopCancellation;

		public Transport()
		{
			mMainThreadActions = new ConcurrentQueue<Action>();
			mMainThreadLoopCancellation = new CancellationTokenSource();
			MainThreadLoop(mMainThreadLoopCancellation.Token);
		}

		private async void MainThreadLoop(CancellationToken cancellationToken)
		{
			while (cancellationToken != null && !cancellationToken.IsCancellationRequested)
			{
				if (mMainThreadActions != null && mMainThreadActions.TryDequeue(out var action))
				{
					action?.Invoke();
				}

				await Task.Delay(50);
			}
		}

		public void Dispose()
		{
			mMainThreadLoopCancellation?.Cancel();
			mMainThreadLoopCancellation = null;
		}
	}
}