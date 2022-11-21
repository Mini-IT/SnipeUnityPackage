using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
		protected MessageBufferProvider _messageBufferProvider = new MessageBufferProvider();

		protected readonly ConcurrentQueue<Action> _mainThreadActions;
		protected CancellationTokenSource _mainThreadLoopCancellation;

		public Transport()
		{
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

		public void Dispose()
		{
			if (_mainThreadActions != null)
			{
				_mainThreadLoopCancellation.Cancel();
				_mainThreadLoopCancellation = null;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected void WriteInt3(byte[] buffer, int offset, int value)
		{
			buffer[offset + 0] = (byte)(value >> 8);
			buffer[offset + 1] = (byte)(value >> 0x10);
			buffer[offset + 2] = (byte)(value >> 0x18);
		}
	}
}