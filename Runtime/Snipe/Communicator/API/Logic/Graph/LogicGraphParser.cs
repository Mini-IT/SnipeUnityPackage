using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	internal class LogicGraphParser
	{
		private readonly ISnipeTable<SnipeTableGraphsItem> _table;

		public LogicGraphParser(ISnipeTable<SnipeTableGraphsItem> table)
		{
			_table = table;
		}

		public bool TryParse(SnipeObject data, out LogicGraph graph)
		{
			int id = data.SafeGetValue<int>("id");

			if (_table.TryGetValue(id, out var tableItem))
			{
				data.TryGetValue("state", out SnipeObject stateData);
				TryParseGraphStete(stateData, out LogicGraphState graphState);
				graph = new LogicGraph(id, tableItem, graphState);
				return true;
			}

			graph = default;
			return false;
		}

		private bool TryParseGraphStete(SnipeObject data, out LogicGraphState state)
		{
			if (data == null)
			{
				state = default;
				return false;
			}

			int nodeID = data.SafeGetValue<int>("id");
			string errorCode = data.SafeGetString("errorCode");
			IDictionary<string, object> nodeVars = null;

			if (data["vars"] is SnipeObject dataVars)
			{
				nodeVars = dataVars;
			}

			state = new LogicGraphState(nodeID, errorCode, nodeVars);
			return true;
		}
	}
}
