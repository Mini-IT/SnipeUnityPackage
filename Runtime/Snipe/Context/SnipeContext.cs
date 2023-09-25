using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe
{
	public class SnipeContext : IDisposable
	{
		#region static

		/// <summary>
		/// An instance of <see cref="SnipeContext"/> that uses an empty string as a <see cref="Id"/>
		/// </summary>
		public static SnipeContext Default => GetInstance();

		/// <summary>
		/// All <see cref="SnipeContext"/>s that have been created. This may include disposed contexts
		/// </summary>
		public static IEnumerable<SnipeContext> All => s_instances.Values;

		private static readonly Dictionary<string, SnipeContext> s_instances = new Dictionary<string, SnipeContext>();
		private static readonly object s_instancesLock = new object();

		/// <summary>
		/// Create or retrieve a <see cref="SnipeContext"/> for the given <see cref="Id"/>.
		/// There is only one instance of a context per <see cref="Id"/>.
		/// </summary>
		/// <param name="id">A named code that represents a player slot on the device. The <see cref="Default"/> context uses an empty string. </param>
		/// <param name="initialize">Create and initialize a new instance if no existing one is found or reinitialize the old one if it was stopped.</param>
		/// <returns>A reference to the <see cref="SnipeContext"/> instance, corresponding to the specified <paramref name="id"/>.
		/// Can return <c>null</c> if <paramref name="initialize"/> is set to <c>false</c></returns>
		public static SnipeContext GetInstance(string id = null, bool initialize = true)
			=> GetInstance<SnipeContext>(id, initialize);

		protected static TContext GetInstance<TContext>(string id = null, bool initialize = true) where TContext : SnipeContext
		{
			id ??= string.Empty;
			TContext context = null;

			lock (s_instancesLock)
			{
				// there should only be one context per instance id.
				if (s_instances.TryGetValue(id, out SnipeContext existingContext))
				{
					if (existingContext is TContext apiContext)
					{
						context = apiContext;

						if (!context.IsDisposed)
						{
							initialize = false;
						}
					}
					else
					{
						throw new InvalidCastException($"Unable to cast {nameof(SnipeContext)} of type '{existingContext.GetType()}' to type '{typeof(TContext)}'");
					}
				}

				if (initialize)
				{
					if (context == null)
					{
						BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
						ConstructorInfo[] constructors = typeof(TContext).GetConstructors(flags);
						ConstructorInfo constructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);

						if (constructor == null)
						{
							throw new Exception($"No parameterless constructor found for type '{typeof(TContext)}'");
						}

						context = (TContext)constructor.Invoke(new object[] { });
					}
					context.Initialize(id);
					s_instances[id] = context;
				}
			}

			return context;
		}

		#endregion static

		/// <summary>
		/// The player's context identifier. The <see cref="Default"/> context uses an empty string,
		/// but you can use values like "Player1" and "Player2" to get two different concurrently running contexts.
		/// </summary>
		public string Id { get; private set; }

		/// <summary>
		/// If the <see cref="Dispose"/> method has been run, this property will return true.
		/// Once a context is disposed, you shouldn't use it anymore.
		/// You can re-initialize the context by using <see cref="Start"/>
		/// </summary>
		public bool IsDisposed { get; private set; }

		public SnipeConfig Config { get; private set; }
		public SnipeCommunicator Communicator { get; private set; }
		public AuthSubsystem Auth { get; private set; }
		public LogReporter LogReporter { get; private set; }

		public bool IsDefault => string.IsNullOrEmpty(Id);

		/// <summary>
		/// Protected constructor. Use <see cref="Default"/> or <see cref="GetInstance(string)"/> to get an instance
		/// </summary>
		protected SnipeContext() { }

		/// <summary>
		/// After a context has been disposed with the <see cref="Dispose"/> method, this method can restart the instance.
		/// <para/>
		/// If the context hasn't been disposed, this method won't do anything meaningful.
		/// </summary>
		/// <returns>The same context instance</returns>
		public SnipeContext Start() => GetInstance(Id);

		/// <summary>
		/// Tear down a <see cref="SnipeContext"/> and notify all internal services that the context should be destroyed.
		/// <para />
		/// If you call <see cref="Start"/> or <see cref="GetInstance"/> with the disposed context's <see cref="Id"/>
		/// after the context has been disposed, then the disposed instance will be reinitialized
		/// </summary>
		public virtual void Dispose()
		{
			if (IsDisposed)
				return;

			IsDisposed = true;

			if (Communicator != null)
			{
				Communicator.Dispose();
				Communicator = null;
			}
		}

		protected virtual void Initialize(string id)
		{
			Id = id;
			IsDisposed = false;

			if (Communicator != null)
			{
				return;
			}

			if (!SnipeServices.IsInitialized)
			{
				SnipeServices.Initialize(new UnitySnipeServicesFactory());
			}

			Config = new SnipeConfig(Id);
			Communicator = new SnipeCommunicator(Config);
			Auth = new AuthSubsystem(Communicator, Config);
			LogReporter = new LogReporter(this);

			UnityTerminator.Run();
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
