using System.Collections.Generic;
using System.Linq;
using MiniIT.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiTables
	{
		public bool Loading => _loading;

		private readonly HashSet<SnipeTable> _tables;
		private readonly TablesLoader _loader;
		private readonly object _lock = new object();
		private AlterTask<bool> _loadingTask;
		private bool _loading = false;

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

		public async AlterTask Load(bool restart = false)
		{
			AlterTask<bool> task;

			lock (_lock)
			{
				if (restart)
				{
					_loadingTask = default;
					_loading = false;
					_loader.Reset();
				}
				else if (Loaded)
				{
					return;
				}

				if (_loading)
				{
					task = _loadingTask;
				}
				else
				{
					task = _loadingTask = _loader.Load();
					_loading = true;
				}
			}

			await task;

			lock (_lock)
			{
				if (task == _loadingTask)
				{
					_loadingTask = default;
					_loading = false;
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
