using System;
using System.Collections.Generic;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public interface ISnipeContextProvider
	{
		bool TryGetContext(int id, out SnipeContext context);
		SnipeContext GetContext(int id);
	}

	public interface ISnipeTablesProvider
	{
		SnipeApiTables GetTables();
	}

	public class SnipeManager : ISnipeContextProvider, ISnipeTablesProvider
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

		private readonly Dictionary<int, SnipeContext> _contexts = new Dictionary<int, SnipeContext>();

		private ISnipeContextFactory _contextFactory;
		private ISnipeApiTablesFactory _tablesFactory;
		private SnipeApiTables _tables;

		public void Initialize(ISnipeContextFactory contextFactory, ISnipeApiTablesFactory tablesFactory)
		{
			_contextFactory = contextFactory;
			_tablesFactory = tablesFactory;
			_ = GetContext(0); // create default context
		}

		public bool TryGetContext(int id, out SnipeContext context)
		{
			return _contexts.TryGetValue(id, out context);
		}

		public SnipeContext GetContext(int id)
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
