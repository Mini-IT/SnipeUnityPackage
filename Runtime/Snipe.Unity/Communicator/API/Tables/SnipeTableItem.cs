using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeTableItem
	{
		public int id;
	}

	public interface ISnipeTableItemsListWrapper
	{
		IList GetList();
	}

	public class SnipeTableItemsListWrapper<TItem> : ISnipeTableItemsListWrapper
	{
		public List<TItem> list { get; set; }
		public IList GetList() => list;
	}
}
