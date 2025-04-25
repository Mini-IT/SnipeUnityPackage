using System;
using System.Collections.Generic;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public interface ISnipeContextProvider
	{
		/// <summary>
		/// Try to get the instance of <see cref="SnipeContext"/>.
		/// If the internal reference is not set yet,
		/// then <b>no instance will be created</b>
		/// </summary>
		/// <param name="id">Context ID</param>
		/// <param name="context">Instance of <see cref="SnipeContext"/></param>
		/// <returns><c>true</c> if a valid intance is found</returns>
		bool TryGetContext(int id, out SnipeContext context);

		/// <summary>
		/// Gets or creates <see cref="SnipeContext"/> with the ID == <paramref name="id"/>
		/// </summary>
		/// <param name="id">Context ID</param>
		/// <returns>Instance of <see cref="SnipeContext"/></returns>
		SnipeContext GetOrCreateContext(int id);
	}

	public interface ISnipeTablesProvider
	{
		SnipeApiTables GetTables();
	}

	public interface ISnipeManager : ISnipeContextProvider, ISnipeTablesProvider
	{
		bool Initialized { get; }
		void Initialize(ISnipeContextFactory contextFactory, ISnipeApiTablesFactory tablesFactory);
	}

	public class SnipeManager : ISnipeManager
	{
		#region Singleton

		public static SnipeManager Instance
		{
			get
			{
				lock (s_instanceLock)
				{
					return s_instance ??= new SnipeManager();
				}
			}
		}

		private static SnipeManager s_instance;
		private static readonly object s_instanceLock = new object();

		// Private constructor prevents the creation of instances other than the singleton.
		private SnipeManager() { }

		#endregion

		public bool Initialized => _contextFactory != null && _tablesFactory != null;

		private readonly Dictionary<int, SnipeContext> _contexts = new ();

		private ISnipeContextFactory _contextFactory;
		private ISnipeApiTablesFactory _tablesFactory;
		private SnipeApiTables _tables;

		public void Initialize(ISnipeContextFactory contextFactory, ISnipeApiTablesFactory tablesFactory)
		{
			_contextFactory = contextFactory;
			_tablesFactory = tablesFactory;
			_ = GetOrCreateContext(0); // create default context
		}

		public bool TryGetContext(int id, out SnipeContext context)
		{
			return _contexts.TryGetValue(id, out context);
		}

		public SnipeContext GetOrCreateContext(int id)
		{
			if (_contexts.TryGetValue(id, out var context))
			{
				return context;
			}

			if (_contextFactory == null)
			{
				throw new NullReferenceException("Snipe context factory is null");
			}

			context = _contextFactory.CreateContext(id);
			_contexts[id] = context;
			return context;
		}

		public SnipeApiTables GetTables()
		{
			if (_tables == null)
			{
				if (_tablesFactory == null)
				{
					throw new NullReferenceException("Snipe tables factory is null");
				}

				_tables = _tablesFactory.CreateSnipeApiTables();
			}
			return _tables;
		}
	}
}
