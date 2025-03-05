using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeManager
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

		// Private constructor prevents creation of instances other than the singleton
		private SnipeManager() { }

		#endregion

		private readonly Dictionary<int, SnipeContext> _contexts = new Dictionary<int, SnipeContext>();

		private ISnipeContextFactory _contextFactory;

		public void SetContextFactory(ISnipeContextFactory contextFactory)
		{
			_contextFactory = contextFactory;
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
	}
}
