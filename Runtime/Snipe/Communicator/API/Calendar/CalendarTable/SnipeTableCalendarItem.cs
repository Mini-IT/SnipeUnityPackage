using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class SnipeTableCalendarItem : SnipeTableItem
	{
		public string name;
		public string stringID;
		public int startDate;
		public int endDate;
		public bool isInfinite;
		public IDictionary<string, object> data;
		public List<SnipeTableCalendarItemStage> stages;
	}
}
