using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public interface ISnipeTable
	{
		bool Loaded { get; }
	}

	public interface ISnipeTable<TItem> : ISnipeTable, IReadOnlyDictionary<int, TItem>
		where TItem : SnipeTableItem, new()
	{

	}
}
