using System;
using System.Collections.Generic;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe
{
	public class SnipeManager : ISnipeManager
	{
		public bool Initialized => _contextFactory != null && _tablesFactory != null;

		private readonly Dictionary<int, SnipeContext> _contexts = new ();
		private readonly Dictionary<int, SnipeContextReference> _references = new ();

		private ISnipeContextFactory _contextFactory;
		private ISnipeApiTablesFactory _tablesFactory;
		private SnipeApiTables _tables;

		public void Initialize(ISnipeContextAndTablesFactory factory)
		{
			Initialize(factory, factory);
		}

		public void Initialize(ISnipeContextFactory contextFactory, ISnipeApiTablesFactory tablesFactory)
		{
			_contextFactory = contextFactory;
			_tablesFactory = tablesFactory;
			// _ = GetOrCreateContext(0); // create default context
		}

		public bool TryGetContext(out SnipeContext context)
		{
			return TryGetContext(0, out context);
		}

		public bool TryGetContext(int id, out SnipeContext context)
		{
			return _contexts.TryGetValue(id, out context);
		}

		public SnipeContext GetOrCreateContext(int id = 0)
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

			if (_references.TryGetValue(id, out var reference))
			{
				reference.SetContext(context);
			}

			return context;
		}

		public ISnipeContextReference GetContextReference(int id = 0)
		{
			if (_references.TryGetValue(id, out var reference))
			{
				return reference;
			}

			var contextReference = new SnipeContextReference();
			_references[id] = contextReference;

			if (_contexts.TryGetValue(id, out var context))
			{
				contextReference.SetContext(context);
			}

			return contextReference;
		}

		public SnipeApiTables GetSnipeTables()
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
