﻿using System;
using System.Collections.Generic;
using System.Threading;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiModuleManagerWithTable : AbstractSnipeApiModuleManager
	{
		private ISnipeTable _table = null;

		protected CancellationTokenSource _waitingTableCancellation;
		private Queue<QueuedMessage> _waitingTableMessages;

		public AbstractSnipeApiModuleManagerWithTable(SnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory,
			ISnipeTable table)
			: base(communicator, requestFactory)
		{
			_table = table;

			if (_table != null && !_table.Loaded)
			{
				_waitingTableCancellation = new CancellationTokenSource();
				WaitForTableLoaded(_waitingTableCancellation.Token);
			}
		}

		protected override void OnSnipeCommunicatorPreDestroy()
		{
			Dispose();
		}

		public override void Dispose()
		{
			CancellationTokenHelper.CancelAndDispose(ref _waitingTableCancellation);

			ClearCommunicatorReference();

			_table = null;
		}

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
