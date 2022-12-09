using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MiniIT.Snipe
{
	public class LogicManager
	{
		public static TimeSpan UpdateMinTimeout = TimeSpan.FromSeconds(30);

		public delegate void LogicUpdatedHandler(Dictionary<int, LogicNode> nodes);
		public delegate void ExitNodeHandler(LogicNode node, List<object> results);

		public event LogicUpdatedHandler LogicUpdated;
		public event ExitNodeHandler ExitNode;

		public Dictionary<int, LogicNode> Nodes { get; private set; } = new Dictionary<int, LogicNode>();
		private Dictionary<string, LogicNode> _taggedNodes;
		private SnipeTable<SnipeTableLogicItem> _logicTable = null;

		private SnipeCommunicator _snipeCommunicator;
		private TimeSpan _updateRequestedTime = TimeSpan.Zero;
		private SnipeObject _logicGetRequestParameters;
		
		private Stopwatch _refTime = Stopwatch.StartNew();
		private Timer _secondsTimer;

		public void Init(SnipeTable<SnipeTableLogicItem> logic_table)
		{
			_logicTable = logic_table;

			if (_snipeCommunicator != null)
			{
				_snipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
				_snipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
				_snipeCommunicator = null;
			}

			_snipeCommunicator = SnipeCommunicator.Instance;
			_snipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
			_snipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
			_snipeCommunicator.MessageReceived += OnSnipeMessageReceived;
			_snipeCommunicator.PreDestroy += OnSnipeCommunicatorPreDestroy;
		}

		~LogicManager()
		{
			Dispose();
		}

		private void OnSnipeCommunicatorPreDestroy()
		{
			Dispose();
		}

		public void Dispose()
		{
			StopSecondsTimer();

			if (_snipeCommunicator != null)
			{
				_snipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
				_snipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
				_snipeCommunicator = null;
			}

			_logicTable = null;
			Nodes.Clear();
			_taggedNodes = null;
		}

		public LogicNode GetNodeById(int id)
		{
			if (Nodes.TryGetValue(id, out var node))
			{
				return node;
			}

			return null;
		}
		
		public LogicNode GetNodeByTreeId(int id)
		{
			foreach (var node in Nodes.Values)
			{
				if (node?.tree != null && node.tree.id == id)
				{
					return node;
				}
			}

			return null;
		}

		public LogicNode GetNodeByName(string name)
		{
			foreach (var node in Nodes.Values)
			{
				if (string.Equals(node?.name, name, StringComparison.Ordinal))
				{
					return node;
				}
			}

			return null;
		}
		
		public LogicNode GetNodeByTreeStringID(string stringID)
		{
			foreach (var node in Nodes.Values)
			{
				if (string.Equals(node?.tree?.stringID, stringID, StringComparison.Ordinal))
				{
					return node;
				}
			}

			return null;
		}

		public LogicNode GetNodeByTag(string tag)
		{
			if (_taggedNodes != null && _taggedNodes.TryGetValue(tag, out var node))
			{
				return node;
			}

			return null;
		}

		public void RequestLogicGet(bool force = false)
		{
			if (_logicTable != null)
			{
				var current_time = _refTime.Elapsed;
				if (!force && current_time - _updateRequestedTime < UpdateMinTimeout)
					return;

				_updateRequestedTime = current_time;
			}
			
			if (_logicGetRequestParameters == null)
				_logicGetRequestParameters = new SnipeObject() { ["noDump"] = false };

			var request = SnipeApiBase.CreateRequest("logic.get", _logicGetRequestParameters);
			request?.Request();
		}

		public void IncVar(LogicNode node, string name)
		{
			IncVar(name, node?.tree?.id ?? 0);
		}

		public void IncVar(string name, int tree_id = 0) //, int amount = 0)
		{
			SnipeObject request_data = new SnipeObject()
			{
				["name"] = name,
			};
			if (tree_id != 0)
				request_data["treeID"] = tree_id;
			//if (amount != 0)
			//	request_data["amount"] = amount;
			var request = SnipeApiBase.CreateRequest("logic.incVar", request_data);
			if (request != null)
			{
				request.Request();
			}

			_updateRequestedTime = TimeSpan.Zero; // reset timer
		}

		private void OnSnipeMessageReceived(string message_type, string error_code, SnipeObject data, int request_id)
		{
			switch (message_type)
			{
				case "logic.get":
					OnLogicGet(error_code, data);
					break;

				case "logic.exitNode":
					OnLogicExitNode(data);
					break;
					
				case "logic.incVar":
					RequestLogicGet(true);
					break;
			}
		}

		private void OnLogicGet(string error_code, SnipeObject response_data)
		{
			_updateRequestedTime = TimeSpan.Zero; // reset timer  // ????????
			
			if (_logicTable == null || response_data == null)
				return;

			if (error_code == "ok")
			{
				var logic_nodes = new List<LogicNode>();
				if (response_data["logic"] is IList src_logic)
				{
					foreach (var o in src_logic)
					{
						if (o is SnipeObject so && so?["node"] is SnipeObject node)
							logic_nodes.Add(new LogicNode(node, _logicTable));
					}
				}
				
				bool timer_finished = false;

				if (Nodes.Count == 0)
				{
					_taggedNodes = new Dictionary<string, LogicNode>();
					foreach (var node in logic_nodes)
					{
						if (node == null)
							continue;
						
						Nodes.Add(node.id, node);
						AddTaggedNode(node);
					}
				}
				else
				{
					_taggedNodes.Clear();

					foreach (var node in logic_nodes)
					{
						if (node != null && node.id > 0)
						{
							if (node.timeleft == 0) // (-1) means that the node does not have a timer
							{
								timer_finished = true;
							}

							if (Nodes.TryGetValue(node.id, out var stored_node))
							{
								stored_node.CopyVars(node);
								AddTaggedNode(stored_node);
							}
							else
							{
								Nodes.Add(node.id, node);
								AddTaggedNode(node);
							}
						}
					}

					List<int> nodes_to_exclude = null;
					foreach (var stored_node_id in Nodes.Keys)
					{
						bool found = false;
						foreach (var node in logic_nodes)
						{
							if (node != null && node.id == stored_node_id)
							{
								found = true;
								break;
							}
						}
						if (!found)
						{
							if (nodes_to_exclude == null)
								nodes_to_exclude = new List<int>();
							nodes_to_exclude.Add(stored_node_id);
						}
					}

					if (nodes_to_exclude != null)
					{
						foreach (int key in nodes_to_exclude)
						{
							Nodes.Remove(key);
						}
					}
				}

				// Highly unlikely but sometimes
				// messages may contain zero timer values (because of rounding).
				// In this case just request once more
				if (timer_finished)
				{
					RequestLogicGet(true);
				}
				else
				{
					LogicUpdated?.Invoke(Nodes);
					StartSecondsTimer();
				}
			}
		}
		
		private void AddTaggedNode(LogicNode node)
		{
			if (node?.tree?.tags != null)
			{
				foreach (string tag in node.tree.tags)
				{
					if (!string.IsNullOrEmpty(tag))
					{
						_taggedNodes[tag] = node;
					}
				}
			}
		}

		private void OnLogicExitNode(SnipeObject data)
		{
			ExitNode?.Invoke(GetNodeById(data.SafeGetValue("id", 0)),
				data["results"] as List<object>);

			RequestLogicGet(true);
		}

		#region SecondsTimer

		private void StartSecondsTimer()
		{
			if (_secondsTimer == null)
				_secondsTimer = new Timer(OnSecondsTimerTick, null, 0, 1000);
		}

		private void StopSecondsTimer()
		{
			if (_secondsTimer == null)
				return;

			_secondsTimer.Dispose();
			_secondsTimer = null;
		}

		private void OnSecondsTimerTick(object state = null)
		{
			if (Nodes.Count > 0)
			{
				bool finished = false;

				foreach (var node in Nodes.Values)
				{
					if (node != null && node.timeleft > 0)
					{
						node.timeleft--;
						if (node.timeleft <= 0)
							finished = true;
					}
				}

				if (finished)
				{
					RequestLogicGet(true);
				}
			}
		}

		#endregion
	}
}