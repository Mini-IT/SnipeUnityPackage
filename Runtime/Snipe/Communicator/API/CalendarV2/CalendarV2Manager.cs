using System;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class CalendarV2Manager : IDisposable
	{
		public TimeZoneInfo ServerTimeZone { get; }

		private ISnipeTable<SnipeTableCalendarV2Item> _calendarTable = null;

		public CalendarV2Manager(ISnipeTable<SnipeTableCalendarV2Item> calendar_table, TimeZoneInfo serverTimeZone)
		{
			_calendarTable = calendar_table;
			ServerTimeZone = serverTimeZone;
		}

		~CalendarV2Manager()
		{
			Dispose();
		}

		public void Dispose()
		{
			_calendarTable = null;
		}
	}
}
