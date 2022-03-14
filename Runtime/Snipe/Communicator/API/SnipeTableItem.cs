using MiniIT;
using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeTableItem
	{
		public int id;
	}
	
	public interface ISnipeTableItemsListWrapper<ItemType>
	{
		List<ItemType> list { get; set; }
	}
}