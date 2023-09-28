using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public abstract class SnipeTable
	{
		public enum LoadingLocation
		{
			Network,  // External URL
			Cache,    // Application cache
			BuiltIn,  // StremingAssets
		}

		public bool Loaded { get; internal set; } = false;
		public bool LoadingFailed { get; internal set; } = false;
		public LoadingLocation LoadedFrom { get; internal set; } = LoadingLocation.Network;

		abstract internal IDictionary GetItems();
	}

	public class SnipeTable<TItem> : SnipeTable, IReadOnlyDictionary<int, TItem>
		where TItem : SnipeTableItem, new()
	{
		internal readonly Dictionary<int, TItem> _items = new Dictionary<int, TItem>();
		public IReadOnlyDictionary<int, TItem> Items => _items;

		internal override IDictionary GetItems() => _items;

		public TItem this[int id]
		{
			get
			{
				TryGetValue(id, out var item);
				return item;
			}
		}

		public bool TryGetValue(int id, out TItem item)
		{
			if (Loaded && _items != null)
			{
				return _items.TryGetValue(id, out item);
			}

			item = default;
			return false;
		}

		public int Count => _items.Count;
		public bool IsReadOnly => true;
		IEnumerable<int> IReadOnlyDictionary<int, TItem>.Keys => _items.Keys;
		IEnumerable<TItem> IReadOnlyDictionary<int, TItem>.Values => _items.Values;
		public bool ContainsKey(int key) => _items.ContainsKey(key);
		public IEnumerator<KeyValuePair<int, TItem>> GetEnumerator() => _items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
	}
}
