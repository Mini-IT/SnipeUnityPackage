using MiniIT;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeTableItem
	{
		public int id;
		
		// TODO: remove
		public virtual void SetData(SnipeObject data)
		{
			this.id = data.SafeGetValue<int>("id");
		}
	}
	
	public interface ISnipeTableItemsListWrapper<ItemType>
	{
		List<ItemType> list { get; set; }
	}
}