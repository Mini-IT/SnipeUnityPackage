using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeTableCalendarV2ItemsWrapper : SnipeTableItemsListWrapper<SnipeTableCalendarV2Item>
	{
		public static SnipeTableCalendarV2ItemsWrapper FromTableData(IDictionary<string, object> tableData)
		{
			if (tableData != null && tableData.TryGetValue("list", value: out object tableListData) && tableListData is IList tableList)
			{
				var calendarListWrapper = new SnipeTableCalendarV2ItemsWrapper
				{
					list = new List<SnipeTableCalendarV2Item>()
				};

				foreach (Dictionary<string, object> calendarItemData in tableList)
				{
					var calendarEvent = new SnipeTableCalendarV2Item();
					calendarListWrapper.list.Add(calendarEvent);

					if (calendarItemData.TryGetValue("id", out object calendarEventID))
					{
						calendarEvent.id = Convert.ToInt32(calendarEventID);
					}

					if (calendarItemData.TryGetValue("name", out object calendarEventName))
					{
						calendarEvent.Name = Convert.ToString(calendarEventName);
					}

					if (calendarItemData.TryGetValue("stringID", out object calendarEventStringID))
					{
						calendarEvent.StringID = Convert.ToString(calendarEventStringID);
					}

					if (calendarItemData.TryGetValue("devOverride", out object calendarEventDevOverride))
					{
						calendarEvent.DevOverride = Convert.ToBoolean(calendarEventDevOverride);
					}

					calendarEvent.Vars = new List<SnipeTableCalendarV2ItemVariable>();

					if (calendarItemData.TryGetValue("vars", out object calendarEventVars) && calendarEventVars is IList calendarEventStagesList)
					{
						foreach (Dictionary<string, object> varItemData in calendarEventStagesList)
						{
							var eventVar = new SnipeTableCalendarV2ItemVariable();
							calendarEvent.Vars.Add(eventVar);

							if (varItemData.TryGetValue("stringID", out object varID))
							{
								eventVar.StringID = Convert.ToString(varID);
							}

							if (varItemData.TryGetValue("type", out object stageType))
							{
								eventVar.Type = Convert.ToString(stageType);
							}

							if (varItemData.TryGetValue("value", out object varValue))
							{
								eventVar.Value = Convert.ToString(varValue);
							}

							if (varItemData.TryGetValue("valueDev", out object stageValueDev))
							{
								eventVar.ValueDev = Convert.ToString(stageValueDev);
							}
						}
					}
				}
				return calendarListWrapper;
			}

			return null;
		}
	}
}
