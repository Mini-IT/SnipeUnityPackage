using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class SnipeTableCalendarV2Item : SnipeTableItem
	{
		public string Name { get; set; }
		public string StringID { get; set; }
		public bool DevOverride { get; set; }
		public List<SnipeTableCalendarV2ItemVariable> Vars { get; set; } = new();

		public T GetValueByName<T>(string name, bool dev = false)
		{
			if (Vars == null)
			{
				return default;
			}

			foreach (var item in Vars)
			{
				if (item.StringID == name)
				{
					return item.GetValue<T>();
				}
			}

			return default;}
	}
}
