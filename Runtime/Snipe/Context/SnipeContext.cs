using System;
using System.Collections.Generic;
using MiniIT.Snipe.Api;

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

		/// <summary>
		/// Create or retrieve a <see cref="SnipeContext"/> for the given <see cref="Id"/>.
		/// There is only one instance of a context per <see cref="Id"/>.
		/// </summary>
		/// <param name="id">A named code that represents a player slot on the device. The <see cref="Default"/> context uses an empty string. </param>
		/// <param name="initialize">Create and initialize a new instance if no existing one is found or reinitialize the old one if it was stopped.</param>
		/// <returns>A reference to the <see cref="SnipeContext"/> instance, corresponding to the specified <paramref name="id"/>.
		/// Can return <c>null</c> if <paramref name="initialize"/> is set to <c>false</c></returns>
		public static SnipeContext GetInstance(string id = null, bool initialize = true)
		{
			id ??= string.Empty;

			SnipeContext context;

			// there should only be one context per instance id.
			if (s_instances.TryGetValue(id, out context))
			{
				if (context.IsStopped && initialize)
				{
					context.Init(id);
				}

				return context;
			}

			if (!initialize)
			{
				return null;
			}

			context = new SnipeContext();
			context.Init(id);
			s_instances[id] = context;
			return context;
		}

		#endregion static

		/// <summary>
		/// The player's context identifier. The <see cref="Default"/> context uses an empty string,
		/// but you can use values like "Player1" and "Player2" to get two different concurrently running contexts.
		/// </summary>
		public string Id { get; private set; }

		/// <summary>
		/// If the <see cref="Stop"/> method has been run, this property will return true. Once a context is disposed, you shouldn't use
		/// it anymore, and the <see cref="ServiceProvider"/> will throw exceptions if you do.
		/// You can re-initialize the context by using <see cref="InParent"/>
		/// </summary>
		public bool IsStopped { get; private set; }

		public SnipeCommunicator Communicator { get; private set; }
		public AuthSubsystem Auth { get; private set; }

		public bool IsDefault => string.IsNullOrEmpty(Id);

		private bool _isStopped;

		/// <summary>
		/// Protected constructor. Use <see cref="Default"/> or <see cref="GetInstance(string)"/> to get an instance
		/// </summary>
		protected SnipeContext() { }

		/// <summary>
		/// After a context has been Stopped with the <see cref="Stop"/> method, this method can restart the instance.
		/// <para/>
		/// If the context hasn't been stopped, this method won't do anything meaningful.
		/// </summary>
		/// <returns>The same context instance</returns>
		public SnipeContext Start() => GetInstance(Id);

		/// <summary>
		/// Tear down a <see cref="SnipeContext"/> and notify all internal services that the context should be destroyed.
		/// <para />
		/// If you call <see cref="Start"/> or <see cref="GetInstance"/> with the disposed context's <see cref="Id"/>
		/// after the context has been disposed, then the disposed instance will be reinitialized
		/// </summary>
		public void Stop()
		{
			if (_isStopped)
				return;

			_isStopped = true;

			_api?.Dispose();
			Communicator?.Dispose();
		}

		public void Dispose()
		{
			Stop();
		}

		protected void Init(string id)
		{
			Id = id;

			if (Communicator != null)
				return;

			Communicator = new SnipeCommunicator();
			Auth = new AuthSubsystem(Communicator);

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

		#region Api

		private AbstractSnipeApiService _api;

		public T GetApiAs<T>() where T : AbstractSnipeApiService
		{
			_api ??= CreateApi<T>();
			return _api as T;
		}

		private AbstractSnipeApiService CreateApi<ApiType>()
		{
			if (_api != null)
				return _api;

			Type type = typeof(ApiType);
			if (type.IsAbstract)
				return null;

			AbstractSnipeApiService.RequestFactoryMethod requestFactory = CreateRequest;

			var constructor = type.GetConstructor(new Type[] { typeof(SnipeCommunicator), typeof(AbstractSnipeApiService.RequestFactoryMethod) });
			return (AbstractSnipeApiService)constructor.Invoke(new object[] { Communicator, requestFactory });
		}

		#endregion Api
	}
}
