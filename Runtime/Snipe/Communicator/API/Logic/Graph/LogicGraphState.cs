using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public struct LogicGraphState
	{
		public int NodeID { get; }
		public string Code { get; }
		public IDictionary<string, object> Vars { get; }

		public LogicGraphState(int nodeID, string code, IDictionary<string, object> vars)
		{
			NodeID = nodeID;
			Code = code;
			Vars = vars;
		}
	}
}
