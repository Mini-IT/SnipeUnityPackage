using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeContext : IDisposable
	{
		/// <summary>
		/// The player's context identifier. The <see cref="Default"/> context id is 0,
		/// but you can use any int values to get different concurrently running contexts.
		/// </summary>
		public int Id { get; }

		/// <summary>
		/// If the <see cref="Dispose"/> method has been run, this property will return true.
		/// Once a context is disposed, you shouldn't use it anymore.
		/// </summary>
		public bool IsDisposed { get; private set; }

		internal SnipeConfig Config { get; }
		public SnipeCommunicator Communicator { get; }
		public AuthSubsystem Auth { get; }
		public LogReporter LogReporter { get; }

		public bool IsDefault => Id == 0;

		/// <summary>
		/// Protected constructor. Use <see cref="Default"/> or <see cref="GetInstance(string)"/> to get an instance
		/// </summary>
		protected SnipeContext(int id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter)
		{
			Id = id;
			Config = config;
			Communicator = communicator;
			Auth = auth;

			logReporter.SetSnipeContext(this);
			LogReporter = logReporter;

			UnityTerminator.AddTarget(this);
		}

		/// <summary>
		/// Tear down a <see cref="SnipeContext"/> and notify all internal services that the context should be destroyed.
		/// <para />
		/// If you call <see cref="Start"/> or <see cref="GetInstance"/> with the disposed context's <see cref="Id"/>
		/// after the context has been disposed, then the disposed instance will be reinitialized
		/// </summary>
		public virtual void Dispose()
		{
			if (IsDisposed)
			{
				return;
			}

			IsDisposed = true;

			Communicator?.Dispose();
			LogReporter?.Dispose();
		}

		public AbstractCommunicatorRequest CreateRequest(string messageType, IDictionary<string, object> data)
		{
			if (Communicator.BatchMode && !Communicator.LoggedIn)
			{
				return new UnauthorizedRequest(Communicator, messageType, data);
			}
			return new SnipeCommunicatorRequest(Communicator, Auth, messageType, data);
		}
	}
}
