using System;

namespace MiniIT.Snipe
{
	public class CalendarManager
	{
		public TimeZoneInfo ServerTimeZone = TimeZoneInfo.CreateCustomTimeZone("server time", TimeSpan.FromHours(4), "server time", "server time");

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
			SnipeTableCalendarItem item = GetItem(eventID);
			if (item != null)
			{
				var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				return now > item.startDate && (item.isInfinite ||  now < item.endDate);
			}

			return false;
		}

		public bool IsEventStageActive(string eventID, string stageID)
		{
			SnipeTableCalendarItem item = GetItem(eventID);
			if (item != null)
			{
				var now_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				if (now_ts > item.startDate && (item.isInfinite || now_ts < item.endDate))
				{
					foreach (var stage in item.stages)
					{
						if (stage.stringID != stageID)
							continue;

						var server_time = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ServerTimeZone);
						if (server_time.Hour < stage.minHour || server_time.Hour >= stage.maxHour)
							return false;

						if (stage.repeatNumber > 1)
						{
							switch (stage.repeatType)
							{
								case "day":
									int day_number = (int)TimeSpan.FromSeconds(now_ts - item.startDate).TotalDays + 1;
									return (day_number % stage.repeatNumber == 1);

								case "week":
									int week_number = (int)(TimeSpan.FromSeconds(now_ts - item.startDate).TotalDays / 7) + 1;
									if (week_number % stage.repeatNumber == 1)
									{

									}
									break;

								case "month":
									
									break;

								//case "none":
								default:

									break;
							}
						}
					}
				}
			}

			return false;
		}

		private SnipeTableCalendarItem GetItem(string eventID)
		{
			if (mCalendarTable?.Items == null)
				return null;

			foreach (var item in mCalendarTable.Items.Values)
			{
				if (item.stringID == eventID)
				{
					return item;
				}
			}

			return null;
		}
	}
}