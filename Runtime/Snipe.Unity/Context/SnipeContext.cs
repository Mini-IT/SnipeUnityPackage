using System;
using System.Collections.Generic;
using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe
{
	public class SnipeContext : IDisposable
	{
		/// <summary>
		/// The player's context identifier. The default context id is 0,
		/// but you can use any int values to get different concurrently running contexts.
		/// </summary>
		public int Id { get; }

		/// <summary>
		/// If the <see cref="Dispose"/> method has been run, this property will return true.
		/// Once a context is disposed, you shouldn't use it anymore.
		/// </summary>
		public bool IsDisposed { get; private set; }

		public string ProjectName { get; private set; }
		public bool IsDev { get; private set; }

		public ISnipeCommunicator Communicator { get; }
		public AuthSubsystem Auth { get; }
		public LogReporter LogReporter { get; }

		/// <summary>
		/// Protected constructor. Use <see cref="SnipeManager"/> to get an instance
		/// </summary>
		protected SnipeContext(int id, ISnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter)
		{
			Id = id;
			Communicator = communicator;
			Auth = auth;
			LogReporter = logReporter;

			UnityTerminator.AddTarget(this);
		}

		internal void Initialize(SnipeConfig config)
		{
			ProjectName = config.ProjectName;
			IsDev = config.Project.Mode == SnipeProjectMode.Dev;

			LogReporter.Initialize(this, config);
		}

		// public void Reset()
		// {
		// 	if (IsDisposed)
		// 	{
		// 		return;
		// 	}
		//
		// 	Communicator.Dispose();
		// 	Auth.ClearAllBindings(); // ??????
		// }

		/// <summary>
		/// Tear down a <see cref="SnipeContext"/> and notify all internal services that the context should be destroyed.
		/// </summary>
		public virtual void Dispose()
		{
			if (IsDisposed)
			{
				return;
			}

			IsDisposed = true;

			Auth.Dispose();
			Communicator.Dispose();
			LogReporter.Dispose();
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
