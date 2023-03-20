using System;
using System.Collections.Generic;
using fbg;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public class SnipeContext : IDisposable
	{
		#region static

		/// <summary>
		/// An instance of <see cref="SnipeContext"/> that uses an empty string as a <see cref="PlayerCode"/>
		/// </summary>
		public static SnipeContext Default => Instantiate();

		/// <summary>
		/// All <see cref="SnipeContext"/>s that have been created. This may include disposed contexts
		/// </summary>
		public static IEnumerable<SnipeContext> All => s_instances.Values;

		private static Dictionary<string, SnipeContext> s_instances = new Dictionary<string, SnipeContext>();

		/// <summary>
		/// Create or retrieve a <see cref="SnipeContext"/> for the given <see cref="PlayerCode"/>. There is only one instance of a context per <see cref="PlayerCode"/>.
		/// </summary>
		/// <param name="playerCode">A named code that represents a player slot on the device. The <see cref="Default"/> context uses an empty string. </param>
		/// <returns></returns>
		public static SnipeContext Instantiate(string playerCode = null)
		{
			playerCode ??= string.Empty;

			SnipeContext context;

			// there should only be one context per playerCode.
			if (s_instances.TryGetValue(playerCode, out context))
			{
				if (context.IsStopped)
				{
					context.Init(playerCode);
				}

				return context;
			}

			context = new SnipeContext();
			context.Init(playerCode);
			s_instances[playerCode] = context;
			return context;
		}

		#endregion static

		/// <summary>
		/// <para>TODO: Rename to InstanceName or something like this</para>
		/// The <see cref="PlayerCode"/> is the name of a player's slot on the device. The <see cref="Default"/> context uses an empty string,
		/// but you could use values like "player1" and "player2" to enable a feature like couch-coop.
		/// </summary>
		public string PlayerCode { get; private set; }

		/// <summary>
		/// If the <see cref="Stop"/> method has been run, this property will return true. Once a context is disposed, you shouldn't use
		/// it anymore, and the <see cref="ServiceProvider"/> will throw exceptions if you do.
		/// You can re-initialize the context by using <see cref="InParent"/>
		/// </summary>
		public bool IsStopped { get; private set; }

		public SnipeCommunicator Communicator { get; private set; }
		public AuthSubsystem Auth { get; private set; }
		public SnipeApiService Api { get; private set; }

		public bool IsDefault => string.IsNullOrEmpty(PlayerCode);

		private bool _isStopped;

		/// <summary>
		/// Protected constructor. Use <see cref="Default"/> or <see cref="Instantiate(string)"/> to get an instance
		/// </summary>
		protected SnipeContext() { }

		/// <summary>
		/// After a context has been Stopped with the <see cref="Stop"/> method, this method can restart the instance.
		/// <para/>
		/// If the context hasn't been stopped, this method won't do anything meaningful.
		/// </summary>
		/// <returns>The same context instance</returns>
		public SnipeContext Start() => Instantiate(playerCode: PlayerCode);

		/// <summary>
		/// Tear down a <see cref="SnipeContext"/> and notify all internal services that the context should be destroyed.
		/// <para />
		/// If you call <see cref="Start"/> or <see cref="Instantiate"/> with the disposed context's <see cref="PlayerCode"/>
		/// after the context has been disposed, then the disposed instance will be reinitialized
		/// </summary>
		public void Stop()
		{
			if (_isStopped)
				return;

			_isStopped = true;

			Api?.Dispose();
			Communicator?.Dispose();
		}

		public void Dispose()
		{
			Stop();
		}

		protected void Init(string playerCode)
		{
			PlayerCode = playerCode;

			if (Communicator != null)
				return;

			Communicator = new SnipeCommunicator();
			Auth = new AuthSubsystem(Communicator);
			Api = new SnipeApiService(Communicator,
				(messageType, data) => new SnipeCommunicatorRequest(Communicator, Auth, messageType, data));

			UnityTerminator.Run();
		}
	}
}
