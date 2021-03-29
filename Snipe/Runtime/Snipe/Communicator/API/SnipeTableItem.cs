using MiniIT;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeTableItem
	{
		//public SnipeObject raw { get; protected set; }

		public int id { get; set; } = 0;

		public SnipeTableItem()
		{
		}
		
		public virtual void SetData(Dictionary<string, object> data)
		{
			SetData(new SnipeObject(data));
		}
		public virtual void SetData(SnipeObject data)
		{
			//this.raw = data;
			this.id = data.SafeGetValue<int>("id");
		}
	}
}