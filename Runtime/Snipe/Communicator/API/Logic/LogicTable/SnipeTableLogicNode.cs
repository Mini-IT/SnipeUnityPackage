using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
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
