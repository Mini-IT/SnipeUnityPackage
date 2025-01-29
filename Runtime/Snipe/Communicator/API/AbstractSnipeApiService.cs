
using System;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiService
	{
		public delegate AbstractCommunicatorRequest RequestFactoryMethod(string messageType, SnipeObject data);

		protected readonly SnipeCommunicator _communicator;
		private readonly RequestFactoryMethod _requestFactory;
		private readonly List<SnipeApiModule> _modules;

		protected internal AbstractSnipeApiService(SnipeCommunicator communicator, AuthSubsystem auth)
		{
			_communicator = communicator;

			_requestFactory = (string messageType, SnipeObject data) =>
			{
				if (communicator.BatchMode && !communicator.LoggedIn)
				{
					return new UnauthorizedRequest(communicator, messageType, data);
				}
				return new SnipeCommunicatorRequest(communicator, auth, messageType, data);
			};

			_modules = new List<SnipeApiModule>();

			InitMergeableRequestTypes();
		}

		public AbstractCommunicatorRequest CreateRequest(string message_type, SnipeObject data = null)
		{
			return _requestFactory.Invoke(message_type, data);
		}

		public void SubscribeOnMessageReceived(SnipeCommunicator.MessageReceivedHandler handler)
		{
			_communicator.MessageReceived -= handler;
			_communicator.MessageReceived += handler;
		}

		public bool TryGetModule<TModule>(out TModule module) where TModule : SnipeApiModule
		{
			for (int i = 0; i < _modules.Count; i++)
			{
				if (_modules[i] is TModule castedModule)
				{
					module = castedModule;
					return true;
				}
			}

			module = null;
			return false;
		}

		internal void AddModule(SnipeApiModule module)
		{
			_modules.Add(module);
		}

		protected virtual void InitMergeableRequestTypes()
		{
		}

		protected void AddMergeableRequestType(SnipeRequestDescriptor descriptor)
		{
			_communicator.MergeableRequestTypes.Add(descriptor);
		}
	}
}
