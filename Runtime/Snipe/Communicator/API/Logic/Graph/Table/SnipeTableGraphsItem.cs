using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class SnipeTableGraphsItem : SnipeTableItem
	{
		public string name;
		public string stringID;
		public string note;
		public List<SnipeTableGraphNode> nodes;
		//public List<SnipeTableGraphLine> lines;
	}
}
