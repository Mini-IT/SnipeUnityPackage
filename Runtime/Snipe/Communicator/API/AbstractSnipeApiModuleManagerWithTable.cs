using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniIT.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiModuleManagerWithTable : IDisposable
	{
		private class QueuedMessage
		{
			internal string _messageType;
			internal string _errorCode;
			internal SnipeObject _data;
		}
		
		private ISnipeTable _table = null;

		protected CancellationTokenSource _waitingTableCancellation;
		private Queue<QueuedMessage> _waitingTableMessages;

		protected SnipeCommunicator _snipeCommunicator;
		protected readonly AbstractSnipeApiService.RequestFactoryMethod _requestFactory;

		public AbstractSnipeApiModuleManagerWithTable(SnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory,
			ISnipeTable table)
		{
			_requestFactory = requestFactory;
			_table = table;

			ClearCommunicatorReference();

			_snipeCommunicator = communicator;

			if (_table != null)
			{
				_snipeCommunicator.MessageReceived += OnSnipeMessageReceived;
				_snipeCommunicator.PreDestroy += OnSnipeCommunicatorPreDestroy;

				if (!_table.Loaded)
				{
					_waitingTableCancellation = new CancellationTokenSource();
					WaitForTableLoaded(_waitingTableCancellation.Token);
				}
			}
		}
		
		protected virtual void OnSnipeCommunicatorPreDestroy()
		{
			Dispose();
		}

		public virtual void Dispose()
		{
			if (_waitingTableCancellation != null)
			{
				_waitingTableCancellation.Cancel();
				_waitingTableCancellation = null;
			}

			ClearCommunicatorReference();

			_table = null;
		}

		protected void ClearCommunicatorReference()
		{
			if (_snipeCommunicator != null)
			{
				_snipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
				_snipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
				_snipeCommunicator = null;
			}
		}

		protected abstract void OnSnipeMessageReceived(string messageType, string errorCode, SnipeObject data, int requestId);

		protected void ProcessMessage(string messageType, string errorCode, SnipeObject data, Action<string, SnipeObject> handler)
		{
			if (_table == null)
			{
				return;
			}

			if (_table.Loaded)
			{
				handler.Invoke(errorCode, data);
				return;
			}

			_waitingTableMessages ??= new Queue<QueuedMessage>();
			_waitingTableMessages.Enqueue(new QueuedMessage() { _messageType = messageType, _errorCode = errorCode, _data = data });
		}

		private async void WaitForTableLoaded(CancellationToken cancellation)
		{
			while (_table != null && !_table.Loaded)
			{
				try
				{
					await AlterTask.Delay(100, cancellation);
				}
				catch (OperationCanceledException)
				{
					return;
				}

				if (cancellation.IsCancellationRequested)
				{
					return;
				}
			}

			if (_waitingTableMessages != null)
			{
				while (_waitingTableMessages.Count > 0)
				{
					QueuedMessage message = _waitingTableMessages.Dequeue();
					OnSnipeMessageReceived(message._messageType, message._errorCode, message._data, 0);
				}
			}
		}
	}
}
