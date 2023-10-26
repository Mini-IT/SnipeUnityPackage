using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class SnipeTableBadgesItem : SnipeTableItem
	{
		public string stringID;
		public string name;
		public bool isDown;
		public int start;
		public List<SnipeTableBadgeLevel> levels;
	}
}
