using System;

namespace MiniIT.Snipe
{
	public class SnipeContext : IDisposable
	{
		/// <summary>
		/// The player's context identifier. The <see cref="Default"/> context uses an empty string,
		/// but you can use values like "Player1" and "Player2" to get two different concurrently running contexts.
		/// </summary>
		public string Id { get; }

		/// <summary>
		/// If the <see cref="Dispose"/> method has been run, this property will return true.
		/// Once a context is disposed, you shouldn't use it anymore.
		/// You can re-initialize the context by using <see cref="Start"/>
		/// </summary>
		public bool IsDisposed { get; private set; }

		public SnipeConfig Config { get; }
		public SnipeCommunicator Communicator { get; }
		public AuthSubsystem Auth { get; }
		public LogReporter LogReporter { get; }

		public bool IsDefault => string.IsNullOrEmpty(Id);

		/// <summary>
		/// Protected constructor. Use <see cref="Default"/> or <see cref="GetInstance(string)"/> to get an instance
		/// </summary>
		protected SnipeContext(string id, SnipeConfig config, SnipeCommunicator communicator, AuthSubsystem auth, LogReporter logReporter)
		{
			Id = id;
			Config = config;
			Communicator = communicator;
			Auth = auth;
			LogReporter = logReporter;

			UnityTerminator.Run();
		}

		/// <summary>
		/// After a context has been disposed with the <see cref="Dispose"/> method, this method can restart the instance.
		/// <para/>
		/// If the context hasn't been disposed, this method won't do anything meaningful.
		/// </summary>
		/// <returns>The same context instance</returns>
		//public void Start() => Construct(); //GetInstance(Id);

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

		public AbstractCommunicatorRequest CreateRequest(string messageType, SnipeObject data)
		{
			if (Communicator.BatchMode && !Communicator.LoggedIn)
			{
				return new UnauthorizedRequest(Communicator, messageType, data);
			}
			return new SnipeCommunicatorRequest(Communicator, Auth, messageType, data);
		}
	}
}
