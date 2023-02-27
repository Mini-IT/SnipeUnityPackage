
using System;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class AbstractSnipeApiService : IDisposable
	{
		protected internal AbstractSnipeApiService()
		{
			AttachCommunicator();
		}

		public SnipeCommunicatorRequest CreateRequest(string message_type, SnipeObject data)
		{
			if (SnipeCommunicator.Instance.LoggedIn || SnipeCommunicator.Instance.AllowRequestsToWaitForLogin)
			{
				return SnipeCommunicator.Instance.CreateRequest(message_type, data);
			}
			
			return null;
		}

		public virtual void Dispose()
		{
			UnsubscribeCommunicatorEvents();
		}

		private async void AttachCommunicator()
		{
			// Allow the subclass constructor to finish
			await Task.Yield();

			while (!SnipeCommunicator.InstanceInitialized)
			{
				await Task.Delay(100);
			}

			if (SnipeCommunicator.Instance.Connected)
			{
				OnConnectionSucceeded();
			}
			else
			{
				SnipeCommunicator.Instance.ConnectionSucceeded += OnConnectionSucceeded;
			}
			SnipeCommunicator.Instance.PreDestroy += OnCommunicatorPreDestroy;

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
			UnsubscribeCommunicatorEvents();
			AttachCommunicator();
		}

		private void UnsubscribeCommunicatorEvents()
		{
			if (SnipeCommunicator.InstanceInitialized)
			{
				SnipeCommunicator.Instance.ConnectionSucceeded -= OnConnectionSucceeded;
				SnipeCommunicator.Instance.PreDestroy -= OnCommunicatorPreDestroy;
			}
		}
	}
}
