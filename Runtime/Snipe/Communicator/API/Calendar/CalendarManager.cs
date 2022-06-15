using System;

namespace MiniIT.Snipe
{
	public class CalendarManager
	{
		private SnipeTable<SnipeTableCalendarItem> mCalendarTable = null;

		public void Init(SnipeTable<SnipeTableCalendarItem> calendar_table)
		{
			mCalendarTable = calendar_table;
		}

		~CalendarManager()
		{
			Dispose();
		}

		public void Dispose()
		{
			mCalendarTable = null;
		}

		public bool IsEventActive(string eventID)
		{
			if (mCalendarTable?.Items == null)
				return false;

			SnipeTableCalendarItem item = null;
			foreach (var itm in mCalendarTable.Items.Values)
			{
				if (itm.stringID == eventID)
				{
					item = itm;
					break;
				}
			}

			if (item != null)
			{
				var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				if (now > item.startDate)
				{
					if (item.isInfinite)
						return true;

					return (now < item.endDate);
				}
			}

			return false;
		}
	}
}