using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MiniIT.Snipe.Unity;

namespace MiniIT.Snipe
{
	public interface ISnipeContextFactory
	{
		SnipeContext CreateContext(string id);
	}

	//public interface ISnipeContextFactory<TContext> : ISnipeContextFactory where TContext : SnipeContext
	//{
	//	new TContext CreateContext();
	//}

	//public class DefaultSnipeContextFactory<TContext> : ISnipeContextFactory<TContext> where TContext : SnipeContext, new()
	//{
	//	SnipeContext ISnipeContextFactory.CreateContext() => CreateContext();
	//	public TContext CreateContext()
	//	{
	//		BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
	//		ConstructorInfo[] constructors = typeof(TContext).GetConstructors(flags);
	//		ConstructorInfo constructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);

	//		if (constructor == null)
	//		{
	//			throw new Exception($"No parameterless constructor found for type '{typeof(TContext)}'");
	//		}

	//		return (TContext)constructor.Invoke(new object[] { });
	//	}
	//}

	public interface ISnipeContextProvider
	{
		//public static SnipeContextProvider Default => GetInstance();

		SnipeContext GetContext(string id = null, bool create = true);
	}

	public class SnipeContextProvider : ISnipeContextProvider
	{
		private readonly ISnipeContextFactory _factory;

		public SnipeContextProvider(ISnipeContextFactory factory)
		{
			_factory = factory;
		}

		/// <summary>
		/// All <see cref="SnipeContext"/>s that have been created. This may include disposed contexts
		/// </summary>
		//public static IEnumerable<SnipeContext> All => s_instances.Values;

		private readonly Dictionary<string, SnipeContext> _instances = new Dictionary<string, SnipeContext>();
		private readonly object _instancesLock = new object();

		/// <summary>
		/// Create or retrieve a <see cref="SnipeContext"/> for the given <see cref="Id"/>.
		/// There is only one instance of a context per <see cref="Id"/>.
		/// </summary>
		/// <param name="id">A named code that represents a player slot on the device. The <see cref="Default"/> context uses an empty string. </param>
		/// <param name="create">Create and initialize a new instance if no existing one is found or reinitialize the old one if it was stopped.</param>
		/// <returns>A reference to the <see cref="SnipeContext"/> instance, corresponding to the specified <paramref name="id"/>.
		/// Can return <c>null</c> if <paramref name="create"/> is set to <c>false</c></returns>
		public SnipeContext GetContext(string id = null, bool create = true)
			=> InternalGetInstance<SnipeContext>(id, create);

		protected TContext InternalGetInstance<TContext>(string id = null, bool initialize = true) where TContext : SnipeContext
		{
			return InternalGetInstance<TContext>(id, _factory);
		}

		protected TContext InternalGetInstance<TContext>(string id, ISnipeContextFactory factory) where TContext : SnipeContext
		{
			id ??= string.Empty;
			TContext context = null;

			lock (_instancesLock)
			{
				// there should only be one context per instance id.
				if (_instances.TryGetValue(id, out SnipeContext existingContext))
				{
					if (existingContext is TContext apiContext)
					{
						context = apiContext;

						if (!context.IsDisposed)
						{
							factory = null;
						}
					}
					else
					{
						throw new InvalidCastException($"Unable to cast {nameof(SnipeContext)} of type '{existingContext.GetType()}' to type '{typeof(TContext)}'");
					}
				}

				if (factory != null)
				{
					context ??= factory.CreateContext(id) as TContext;
					//context.Construct(id); // TODO: check null (TContext)
					_instances[id] = context;
				}
			}

			return context;
		}
	}

	//============================

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
