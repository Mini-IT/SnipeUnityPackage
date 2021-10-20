using System.Collections.Generic;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeAuthChannel : SnipeChannel
	{
		public override bool CheckReady()
		{
			return SnipeCommunicator.InstanceInitialized && SnipeCommunicator.Instance.Connected;
		}
		
		protected override void Initialize()
		{
			if (CheckReady())
				return;
			
			if (SnipeCommunicator.InstanceInitialized)
			{
				SnipeCommunicator.Instance.ConnectionSucceeded += OnCommunicatorConnectionSucceeded;
			}
		}
		
		private void OnCommunicatorConnectionSucceeded()
		{
			RaiseGotReady();
		}
	}
}