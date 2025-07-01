using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class CalendarV2Item
	{
		public int id;
		public SnipeTableCalendarV2Item node { get; private set; }

		public string name { get => node?.Name; }
		public string stringID { get => node?.StringID; }
		public List<SnipeTableCalendarV2ItemVariable> stages { get => node?.Vars; }

		public int timeleft = -1; // seconds left. (-1) means that the node does not have a timer
		public bool isTimeout { get; private set; }

		public CalendarV2Item(SnipeObject data, ISnipeTable<SnipeTableCalendarV2Item> table)
		{
			id = data.SafeGetValue<int>("id");
		}
	}
}
