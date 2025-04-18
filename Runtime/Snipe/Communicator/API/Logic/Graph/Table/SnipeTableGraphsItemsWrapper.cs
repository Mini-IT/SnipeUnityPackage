using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeTableGraphsItemsWrapper : SnipeTableItemsListWrapper<SnipeTableGraphsItem>
	{
		public static SnipeTableGraphsItemsWrapper FromTableData(IDictionary<string, object> tableData)
		{
			if (tableData == null || !tableData.TryGetValue("list", out var tableListData) ||
			    tableListData is not IList tableList)
			{
				return null;
			}

			var graphsListWrapper = new SnipeTableGraphsItemsWrapper()
			{
				list = new List<SnipeTableGraphsItem>()
			};

			foreach (Dictionary<string, object> treeData in tableList)
			{
				var graph = new SnipeTableGraphsItem();
				graphsListWrapper.list.Add(graph);

				if (treeData.TryGetValue("id", out var graphID))
					graph.id = Convert.ToInt32(graphID);
				if (treeData.TryGetValue("name", out var graphName))
					graph.name = Convert.ToString(graphName);
				if (treeData.TryGetValue("stringID", out var graphStringID))
					graph.stringID = Convert.ToString(graphStringID);

				graph.nodes = new List<SnipeTableGraphNode>();

				if (treeData.TryGetValue("nodes", out var treeNodes) && treeNodes is IList treeNodesList)
				{
					foreach (Dictionary<string, object> nodeData in treeNodesList)
					{
						var node = new SnipeTableGraphNode();
						graph.nodes.Add(node);

						if (nodeData.TryGetValue("id", out var nodeID))
							node.id = Convert.ToInt32(nodeID);
						if (nodeData.TryGetValue("type", out var nodeType))
							node.type = Convert.ToString(nodeType);
					}
				}
			}

			return graphsListWrapper;
		}
	}
}
