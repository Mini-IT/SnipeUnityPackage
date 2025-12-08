using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public partial class SnipeCommunicator
	{

		/// <summary>
		/// Connection successfully establisched
		/// </summary>
		public event Action ConnectionEstablished;

		/// <summary>
		/// Connection is completely lost. No reties left
		/// </summary>
		public event Action ConnectionClosed;

		/// <summary>
		/// Connection failed or lost
		/// </summary>
		public event Action ConnectionDisrupted;

		/// <summary>
		/// Automatic connection recovery routine initiated
		/// </summary>
		public event Action ReconnectionScheduled;

		/// <summary>
		/// A message from the server is received
		/// </summary>
		public event MessageReceivedHandler MessageReceived;

		/// <summary>
		/// Disposal routine is initiated
		/// </summary>
		public event Action PreDestroy;

		#region Safe events raising

		private SafeEventRaiser _safeEventRaiser;

		private void RaiseEvent(Delegate eventDelegate, params object[] args)
		{
			_safeEventRaiser ??= new SafeEventRaiser((handler, e) =>
			{
				_logger.LogError($"RaiseEvent - Error in the handler {handler?.Method?.Name}: {e}");
			});

			_safeEventRaiser.RaiseEvent(eventDelegate, args);
		}

		#endregion
	}
}
