using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeTableLogicItemsWrapper : ISnipeTableItemsListWrapper<SnipeTableLogicItem>
	{
		public List<SnipeTableLogicItem> list { get; set; }
	}
	
	[System.Serializable]
	public class SnipeTableLogicItem : SnipeTableItem
	{
		public string name;
		public string stringID;
		public List<string> tags;
		public int entryNodeID;
		public int parentID;
		public List<SnipeTableLogicNode> nodes;
	}
	
	[System.Serializable]
	public class SnipeTableLogicNode
	{
		public int id;
		public string name;
		public string stringID;
		public string note;
		public bool hasConfirm;
		public bool canClientFail;
		public bool sendProgress;
		public List<SnipeTableLogicRawNodeRequire> requires;
		public List<SnipeTableLogicRawNodeVar> vars;
		public List<SnipeTableLogicRawNodeCheck> checks;
		public List<SnipeTableLogicRawNodeResult> results;
	}
}