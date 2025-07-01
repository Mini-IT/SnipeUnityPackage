using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class SnipeTableCalendarV2Item : SnipeTableItem
	{
		public int ID { get; set; }
		public string Name { get; set; }
		public string StringID { get; set; }
		public bool DevOverride { get; set; }
		public List<SnipeTableCalendarV2ItemVariable> Vars { get; set; } = new();
	}
}
