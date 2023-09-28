using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeTableBadgesItemsWrapper : SnipeTableItemsListWrapper<SnipeTableBadgesItem>
	{
		public static SnipeTableBadgesItemsWrapper FromTableData(IDictionary<string, object> tableData)
		{
			if (tableData != null && tableData.TryGetValue("list", out var tableListData) && tableListData is IList tableList)
			{
				var listWrapper = new SnipeTableBadgesItemsWrapper();
				listWrapper.list = new List<SnipeTableBadgesItem>();
				foreach (Dictionary<string, object> itemData in tableList)
				{
					var items = new SnipeTableBadgesItem();
					listWrapper.list.Add(items);

					if (itemData.TryGetValue("id", out var item_id))
						items.id = Convert.ToInt32(item_id);
					if (itemData.TryGetValue("name", out var item_name))
						items.name = Convert.ToString(item_name);
					if (itemData.TryGetValue("stringID", out var item_stringID))
						items.stringID = Convert.ToString(item_stringID);
					if (itemData.TryGetValue("isDown", out var item_isDown))
						items.isDown = Convert.ToBoolean(item_isDown);
					if (itemData.TryGetValue("ToInt32", out var item_start))
						items.start = Convert.ToInt32(item_start);
					if (itemData.TryGetValue("ToInt32", out var item_target))
						items.start = Convert.ToInt32(item_target);
					
					items.levels = new List<SnipeTableBadgeLevel>();
					if (itemData.TryGetValue("levels", out var itemLevels) && itemLevels is IList itemLevelsList)
					{
						foreach (var levelsListItem in itemLevelsList)
						{
							if (levelsListItem is IDictionary<string, object> levelItem)
							{
								var badgeLevel = new SnipeTableBadgeLevel();
								items.levels.Add(badgeLevel);

								if (levelItem.TryGetValue("id", out var badgeLevel_id))
									badgeLevel.id = Convert.ToInt32(badgeLevel_id);
								if (levelItem.TryGetValue("name", out var badgeLevel_name))
									badgeLevel.name = Convert.ToString(badgeLevel_name);
								if (levelItem.TryGetValue("target", out var badgeLevel_target))
									badgeLevel.target = Convert.ToInt32(badgeLevel_target);
							}
						}
					}
				}
				return listWrapper;
			}
			
			return null;
		}
	}
}
