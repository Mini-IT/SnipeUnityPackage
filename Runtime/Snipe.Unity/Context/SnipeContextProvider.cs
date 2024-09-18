using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public interface ISnipeContextFactory
	{
		SnipeContext CreateContext(int id);
	}

	public interface ISnipeContextProvider
	{
		//public static SnipeContextProvider Default => GetInstance();

		SnipeContext GetContext(int id = 0, bool create = true);
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

		private readonly Dictionary<int, SnipeContext> _instances = new Dictionary<int, SnipeContext>();
		private readonly object _instancesLock = new object();

		/// <summary>
		/// Create or retrieve a <see cref="SnipeContext"/> for the given <see cref="Id"/>.
		/// There is only one instance of a context per <see cref="Id"/>.
		/// </summary>
		/// <param name="id">A code that represents a player slot on the device. The <see cref="Default"/> context id is 0. </param>
		/// <param name="create">Create and initialize a new instance if no existing one is found or reinitialize the old one if it was stopped.</param>
		/// <returns>A reference to the <see cref="SnipeContext"/> instance, corresponding to the specified <paramref name="id"/>.
		/// Can return <c>null</c> if <paramref name="create"/> is set to <c>false</c></returns>
		public SnipeContext GetContext(int id = 0, bool create = true)
		{
			return InternalGetInstance<SnipeContext>(id, create);
		}

		protected TContext InternalGetInstance<TContext>(int id = 0, bool create = true) where TContext : SnipeContext
		{
			return InternalGetInstance<TContext>(id, create ? _factory : null);
		}

		protected TContext InternalGetInstance<TContext>(int id, ISnipeContextFactory factory) where TContext : SnipeContext
		{
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
}
