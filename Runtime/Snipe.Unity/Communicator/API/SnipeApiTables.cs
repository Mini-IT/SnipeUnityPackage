using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiTables
	{
		public bool Loading => _loadingTask?.IsCompleted ?? false;

		private readonly HashSet<SnipeTable> _tables;
		private readonly TablesLoader _loader;
		private readonly object _lock = new object();
		private Task<bool> _loadingTask;

		public SnipeApiTables()
		{
			_tables = new HashSet<SnipeTable>();
			_loader = new TablesLoader();
		}
		
		public SnipeTable<ItemType> RegisterTable<ItemType>(SnipeTable<ItemType> table, string name)
			where ItemType : SnipeTableItem, new()
		{
			lock (_lock)
			{
				if (_tables.Add(table))
				{
					_loader.Add(table, name);
				}
			}
			return table;
		}

		public SnipeTable<ItemType> GetTable<ItemType>()
			where ItemType : SnipeTableItem, new()
		{
			lock (_lock)
			{
				return (SnipeTable<ItemType>)_tables.FirstOrDefault(t => t is SnipeTable<ItemType>);
			}
		}

		public async Task Load(bool restart = false)
		{
			Task<bool> task = null;

			lock (_lock)
			{
				if (restart)
				{
					_loadingTask = null;
					_loader.Reset();
				}
				else if (Loaded)
				{
					return;
				}

				task = _loadingTask ??= _loader.Load();
			}

			await task;

			lock (_lock)
			{
				if (task == _loadingTask)
				{
					_loadingTask = null;
				}
			}
		}

		public bool Loaded
		{
			get
			{
				lock (_lock)
				{
					if (_tables.Count == 0)
						return true;
					
					foreach (var table in _tables)
					{
						if (table == null)
							continue;
						if (!table.Loaded)
							return false;
					}
					
					return true;
				}
			}
		}

		public bool LoadingFailed
		{
			get
			{
				lock (_lock)
				{
					if (_tables.Count == 0)
						return false;
					
					foreach (var table in _tables)
					{
						if (table == null)
							continue;
						if (table.LoadingFailed)
							return true;
					}
					
					return false;
				}
			}
		}
	}
}
