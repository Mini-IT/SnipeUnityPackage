using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiTables
	{
		public bool Loading { get; private set; } = false;
		
		private readonly HashSet<SnipeTable> _tables;
		private readonly HashSet<Action> _loadMethods;
		private readonly TablesLoader _loader;
		private readonly object _lock = new object();
		
		public SnipeApiTables()
		{
			_tables = new HashSet<SnipeTable>();
			_loadMethods = new HashSet<Action>();
			_loader = new TablesLoader();
		}
		
		public SnipeTable<ItemType> RegisterTable<ItemType, WrapperType>(SnipeTable<ItemType> table, string name)
			where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
			where ItemType : SnipeTableItem, new()
		{
			lock (_lock)
			{
				_tables.Add(table);
				_loadMethods.Add(() => _loader.Add<ItemType, WrapperType>(table, name));
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
			if (Loading)
			{
				if (!restart)
					return;

				_loader.Reset();
				
				while (Loading)
				{
					await Task.Delay(100);
				}
			}
			
			lock (_lock)
			{
				if (_tables.Count > 0)
				{
					Loading = true;
					_loader.Reset();
					foreach (var method in _loadMethods)
					{
						method?.Invoke();
					}
				}
			
			}
			await _loader.Load();

			Loading = false;
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
