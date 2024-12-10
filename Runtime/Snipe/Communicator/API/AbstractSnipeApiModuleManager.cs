using System;
using System.Collections.Generic;
using System.Threading;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiModuleManager : IDisposable
	{
		protected class QueuedMessage
		{
			internal string _messageType;
			internal string _errorCode;
			internal SnipeObject _data;
		}

		protected SnipeCommunicator _snipeCommunicator;
		protected readonly AbstractSnipeApiService.RequestFactoryMethod _requestFactory;

		public AbstractSnipeApiModuleManager(SnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory)
		{
			ClearCommunicatorReference();

			_requestFactory = requestFactory;
			_snipeCommunicator = communicator;
		}

		protected virtual void OnSnipeCommunicatorPreDestroy()
		{
			Dispose();
		}

		public virtual void Dispose()
		{
			ClearCommunicatorReference();
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
	}
}
