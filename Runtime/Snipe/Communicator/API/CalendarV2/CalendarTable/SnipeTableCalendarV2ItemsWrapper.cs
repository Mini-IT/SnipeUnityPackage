using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeTableCalendarV2ItemsWrapper : SnipeTableItemsListWrapper<SnipeTableCalendarV2Item>
	{
		public static SnipeTableCalendarV2ItemsWrapper FromTableData(IDictionary<string, object> table_data)
		{
			if (table_data != null && table_data.TryGetValue("list", out var table_list_data) && table_list_data is IList table_list)
			{
				var calendar_list_wrapper = new SnipeTableCalendarV2ItemsWrapper();
				calendar_list_wrapper.list = new List<SnipeTableCalendarV2Item>();
				foreach (Dictionary<string, object> calendar_item_data in table_list)
				{
					var calendar_event = new SnipeTableCalendarV2Item();
					calendar_list_wrapper.list.Add(calendar_event);

					if (calendar_item_data.TryGetValue("id", out var calendar_event_id))
						calendar_event.ID = Convert.ToInt32(calendar_event_id);
					if (calendar_item_data.TryGetValue("name", out var calendar_event_name))
						calendar_event.Name = Convert.ToString(calendar_event_name);
					if (calendar_item_data.TryGetValue("stringID", out var calendar_event_stringID))
						calendar_event.StringID = Convert.ToString(calendar_event_stringID);
					if (calendar_item_data.TryGetValue("devOverride ", out var calendar_event_DevOverride ))
						calendar_event.DevOverride = Convert.ToBoolean(calendar_event_DevOverride);

					calendar_event.Vars = new List<SnipeTableCalendarV2ItemVariable>();

					if (calendar_item_data.TryGetValue("vars", out var calendar_event_vars) && calendar_event_vars is IList calendar_event_stages_list)
					{
						foreach (Dictionary<string, object> var_item_data in calendar_event_stages_list)
						{
							var eventVar = new SnipeTableCalendarV2ItemVariable();
							calendar_event.Vars.Add(eventVar);

							if (var_item_data.TryGetValue("stringID", out var var_id))
								eventVar.StringID = Convert.ToString(var_id);
							if (var_item_data.TryGetValue("type", out var stage_type))
								eventVar.Type = Convert.ToString(stage_type);
							if (var_item_data.TryGetValue("value", out var var_value))
								eventVar.Value = Convert.ToString(var_value);
							if (var_item_data.TryGetValue("valueDev", out var stage_value_dev))
								eventVar.ValueDev = Convert.ToString(stage_value_dev);
						}
					}
				}
				return calendar_list_wrapper;
			}

			return null;
		}
	}
}
