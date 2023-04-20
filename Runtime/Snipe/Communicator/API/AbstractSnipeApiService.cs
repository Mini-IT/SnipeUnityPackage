
using System;

namespace MiniIT.Snipe.Api
{
	public class AbstractSnipeApiService : IDisposable
	{
		public delegate AbstractCommunicatorRequest RequestFactoryMethod(string messageType, SnipeObject data);

		public SnipeCommunicator Communicator => _communicator;
		public TimeZoneInfo ServerTimeZone { get; protected set; }

		protected readonly SnipeCommunicator _communicator;
		private readonly RequestFactoryMethod _requestFactory;

		protected internal AbstractSnipeApiService(SnipeCommunicator communicator, RequestFactoryMethod requestFactory)
		{
			_communicator = communicator;
			_requestFactory = requestFactory;

			InitMergeableRequestTypes();
		}

		public AbstractCommunicatorRequest CreateRequest(string message_type, SnipeObject data = null)
		{
			return _requestFactory.Invoke(message_type, data);
		}

		public virtual void Dispose()
		{
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
