using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class CalendarItem
	{
		public int id;

		public SnipeTableCalendarItem node { get; private set; }

		public string name { get => node?.name; }
		public string stringID { get => node?.stringID; }
		public List<SnipeTableCalendarItemStage> stages { get => node?.stages; }

		public int timeleft = -1; // seconds left. (-1) means that the node does not have a timer
		public bool isTimeout { get; private set; }

		public CalendarItem(IDictionary<string, object> data, ISnipeTable<SnipeTableCalendarItem> table)
		{
			id = data.SafeGetValue<int>("id");
		}
	}
}
