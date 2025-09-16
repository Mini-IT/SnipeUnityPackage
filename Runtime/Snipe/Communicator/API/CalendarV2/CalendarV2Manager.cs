using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class CalendarV2Manager : IDisposable
	{
		public TimeZoneInfo ServerTimeZone { get; }

		private ISnipeTable<SnipeTableCalendarV2Item> _calendarTable = null;

		public CalendarV2Manager(ISnipeTable<SnipeTableCalendarV2Item> calendarTable, TimeZoneInfo serverTimeZone)
		{
			_calendarTable = calendarTable;
			ServerTimeZone = serverTimeZone;
		}

		public T GetValueByName<T>(int eventID, string name, bool dev = false)
		{
			if (_calendarTable == null)
			{
				return default;
			}

			foreach (var item in _calendarTable.Values)
			{
				if (item.id == eventID)
				{
					bool devOverride = item.DevOverride;
					dev = dev && devOverride;
					return item.GetValueByName<T>(name, dev);
				}
			}

			return default;
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
