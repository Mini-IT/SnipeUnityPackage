
using System;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class AbstractSnipeApiService : IDisposable
	{
		public delegate SnipeCommunicatorRequest RequestFactoryMethod(string messageType, SnipeObject data);

		public LogicManager LogicManager { get; }
		public CalendarManager CalendarManager { get; }

		public SnipeCommunicator Communicator => _communicator;

		protected readonly SnipeCommunicator _communicator;
		private RequestFactoryMethod _requestFactory;

		protected internal AbstractSnipeApiService(SnipeCommunicator communicator, RequestFactoryMethod requestFactory)
		{
			_communicator = communicator;
			_requestFactory = requestFactory;
			AttachCommunicator(_communicator);
			LogicManager = new LogicManager(this);
			CalendarManager = new CalendarManager();
		}

		public SnipeCommunicatorRequest CreateRequest(string message_type, SnipeObject data)
		{
			if (_communicator.LoggedIn || _communicator.AllowRequestsToWaitForLogin)
			{
				return _requestFactory.Invoke(message_type, data);
			}
			
			return null;
		}

		public virtual void Dispose()
		{
			UnsubscribeCommunicatorEvents(_communicator);

			LogicManager?.Dispose();
			CalendarManager?.Dispose();
		}

		private async void AttachCommunicator(SnipeCommunicator communicator)
		{
			// Allow the subclass constructor to finish
			await Task.Yield();

			//while (!SnipeCommunicator.InstanceInitialized)
			//{
			//	await Task.Delay(100);
			//}

			if (communicator.Connected)
			{
				OnConnectionSucceeded();
			}
			else
			{
				communicator.ConnectionSucceeded += OnConnectionSucceeded;
			}
			communicator.PreDestroy += OnCommunicatorPreDestroy;

			InitMergeableRequestTypes();
		}

		protected virtual void OnConnectionSucceeded()
		{
		}

		protected virtual void InitMergeableRequestTypes()
		{
		}

		protected virtual void OnCommunicatorPreDestroy()
		{
			UnsubscribeCommunicatorEvents(_communicator);
			//AttachCommunicator();
		}

		private void UnsubscribeCommunicatorEvents(SnipeCommunicator communicator)
		{
			if (communicator != null)
			{
				communicator.ConnectionSucceeded -= OnConnectionSucceeded;
				communicator.PreDestroy -= OnCommunicatorPreDestroy;
			}
		}
	}
}
