using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe;

namespace MiniIT.Snipe.Api
{
	public class LogicNode
	{
		public int id;

		public SnipeTableLogicItem tree { get; private set; }
		public SnipeTableLogicNode node { get; private set; }
		public List<SnipeLogicNodeVar> vars { get; private set; }

		public string name => node?.name;
		public string stringID => node?.stringID;
		public List<SnipeTableLogicRawNodeResult> results => node?.results;

		public int timeleft = -1; // seconds left. (-1) means that the node does not have a timer
		public bool isTimeout { get; private set; }

		public LogicNode(IDictionary<string, object> data, ISnipeTable<SnipeTableLogicItem> logicTable, ISnipeServices services)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			id = data.SafeGetValue<int>("id");

			foreach (var tableTree in logicTable.Values)
			{
				foreach (var tableNode in tableTree.nodes)
				{
					if (tableNode.id == id)
					{
						tree = tableTree;
						node = tableNode;
						break;
					}
				}

				if (node != null)
				{
					break;
				}
			}

			if (node == null)
			{
				var logger = services.LoggerFactory.CreateLogger(nameof(LogicNode));
				logger.LogError($"Table node not found. id = {id}");
				return;
			}

			foreach (var nodeCheck in node.checks)
			{
				RefreshTimerVar(nodeCheck.type, nodeCheck.value);
			}

			if (data.TryGetValue("vars", out object value) && value is IList dataVars)
			{
				vars = new List<SnipeLogicNodeVar>(Math.Max(dataVars.Count, node.vars.Count));

				foreach (IDictionary<string, object> dataVar in dataVars)
				{
					if (dataVar == null)
					{
						continue;
					}

					bool found = false;

					string varName = dataVar.SafeGetString("name");

					foreach (var nodeVar in node.vars)
					{
						if (varName == nodeVar.name)
						{
							vars.Add(new SnipeLogicNodeVar()
							{
								var = nodeVar,
								value = dataVar.SafeGetValue<int>("value"),
								maxValue = dataVar.SafeGetValue<int>("maxValue"),
							});

							found = true;
							break;
						}
					}

					if (found)
					{
						continue;
					}

					string varType = dataVar.SafeGetString("type");
					if (!string.IsNullOrEmpty(varType))
					{
						foreach (var nodeVar in node.checks)
						{
							if (varType == nodeVar.type)
							{
								int varValue = dataVar.SafeGetValue<int>("value");
								vars.Add(new SnipeLogicNodeVar()
								{
									var = nodeVar,
									value = varValue,
									maxValue = dataVar.SafeGetValue<int>("maxValue"),
								});

								RefreshTimerVar(varType, varValue);

								// found = true;
								break;
							}
						}
					}
				}
			}
		}

		public bool HasCheckType(string check_type)
		{
			if (node == null)
			{
				return false;
			}

			foreach (var node_check in node.checks)
			{
				if (node_check.type == check_type)
				{
					return true;
				}
			}

			return false;
		}

		public bool HasCheckName(string check_name)
		{
			if (node == null)
			{
				return false;
			}

			foreach (var node_check in node.checks)
			{
				if (node_check.name == check_name)
				{
					return true;
				}
			}

			return false;
		}

		public string GetPurchaseProductSku()
		{
			if (node == null)
			{
				return null;
			}

			foreach (var node_check in node.checks)
			{
				if (node_check.type == SnipeTableLogicNodeCheck.TYPE_PAYMENT_ITEM_STRING_ID)
				{
					return node_check.name;
				}
			}

			return null;
		}

		public void CopyVars(LogicNode src_node)
		{
			if (src_node?.vars == null)
			{
				return;
			}

			vars = src_node.vars;

			foreach (var node_var in vars)
			{
				RefreshTimerVar(node_var.var.type, node_var.value);
			}
		}

		private bool RefreshTimerVar(string var_type, int var_value)
		{
			bool is_timeout = (var_type == SnipeTableLogicNodeCheck.TYPE_TIMEOUT);
			if (is_timeout || var_type == SnipeTableLogicNodeCheck.TYPE_TIMER)
			{
				this.timeleft = var_value;
				this.isTimeout = is_timeout;

				return true;
			}

			return false;
		}
	}

	public class SnipeLogicNodeVar
	{
		public SnipeTableLogicNodeVar @var;
		public int value;
		public int maxValue;

		public string name => var?.name;
		public float condValue => var is SnipeTableLogicNodeVarCondCounter ccVar ? ccVar.condValue : default;
	}
}
