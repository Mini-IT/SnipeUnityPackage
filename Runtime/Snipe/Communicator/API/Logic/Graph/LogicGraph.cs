using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class LogicGraph
	{
		public int ID { get; }
		public LogicGraphState State { get; }

		public LogicGraph(SnipeObject data)
		{
			ID = data.SafeGetValue<int>("id");
			State = (data["state"] is SnipeObject stateData) ? new LogicGraphState(stateData) : null;
		}
	}

	public class LogicGraphState
	{
		public int NodeID { get; }
		public string Code { get; }
		public IDictionary<string, object> Vars { get; }

		public LogicGraphState(SnipeObject data)
		{
			NodeID = data.SafeGetValue<int>("id");
			Code = data.SafeGetString("errorCode");

			if (data["vars"] is SnipeObject dataVars)
			{
				Vars = dataVars;
			}
		}
	}
}
