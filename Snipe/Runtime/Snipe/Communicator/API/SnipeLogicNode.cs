using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeLogicNode
	{
		public int id;

		public SnipeTableLogicItem tree { get; private set; }
		public SnipeTableLogicNode node { get; private set; }
		public List<SnipeLogicNodeVar> vars { get; private set; }

		public string name { get => node?.name; }
		public List<SnipeTableLogicRawNodeResult> results { get => node?.results; }

		public SnipeLogicNode(SnipeObject data, SnipeTable<SnipeTableLogicItem> logic_table)
		{
			id = data.SafeGetValue<int>("id");
			
			foreach (var table_tree in logic_table.Items.Values)
			{
				foreach (var table_node in table_tree.nodes)
				{
					if (table_node.id == id)
					{
						tree = table_tree;
						node = table_node;
						break;
					}
				}

				if (node != null)
					break;
			}

			if (node != null)
			{
				if (data["vars"] is IList data_vars)
				{
					vars = new List<SnipeLogicNodeVar>(node.vars.Count);

					foreach (var v in node.vars)
					{
						SnipeObject data_var = null;
						foreach (SnipeObject dv in data_vars)
						{
							if (dv.SafeGetString("name") == v.name)
							{
								data_var = dv;
								break;
							}
						}

						vars.Add(new SnipeLogicNodeVar()
						{
							var = v,
							value = data_var?.SafeGetValue<int>("value") ?? default,
							maxValue = data_var?.SafeGetValue<int>("maxValue") ?? default,
						});
					}
				}
			}
		}
	}

	public class SnipeLogicNodeVar
	{
		public SnipeTableLogicNodeVar @var;
		public int value;
		public int maxValue;

		public string name { get => var?.name; }
		public float condValue { get => var is SnipeTableLogicNodeVarCondCounter cc_var ? cc_var.condValue : default; }
	}
}