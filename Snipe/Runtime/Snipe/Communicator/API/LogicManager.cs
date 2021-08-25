using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace MiniIT.Snipe
{
	public class LogicManager
	{
		public static float UpdateMinTimeout = 30.0f; // seconds

		public delegate void LogicUpdatedHandler(Dictionary<int, SnipeLogicNode> nodes);
		public delegate void ExitNodeHandler(SnipeLogicNode node, List<object> results);

		public event LogicUpdatedHandler LogicUpdated;
		public event ExitNodeHandler ExitNode;

		public Dictionary<int, SnipeLogicNode> Nodes { get; private set; }
		private Dictionary<string, SnipeLogicNode> mTaggedNodes;
		private SnipeTable<SnipeTableLogicItem> mLogicTable = null;

		private float mUpdateRequestedTime = 0.0f;
		private bool mUpdateRequested = false;

		private SnipeCommunicator mSnipeCommunicator;

		private Timer mSecondsTimer;

		public void Init(SnipeTable<SnipeTableLogicItem> logic_table)
		{
			mLogicTable = logic_table;

			if (mSnipeCommunicator != null)
			{
				mSnipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
				mSnipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
				mSnipeCommunicator = null;
			}

			mSnipeCommunicator = SnipeCommunicator.Instance;
			mSnipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
			mSnipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
			mSnipeCommunicator.MessageReceived += OnSnipeMessageReceived;
			mSnipeCommunicator.PreDestroy += OnSnipeCommunicatorPreDestroy;
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

			if (mSnipeCommunicator != null)
			{
				mSnipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
				mSnipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
				mSnipeCommunicator = null;
			}

			mLogicTable = null;
			Nodes = null;
			mTaggedNodes = null;
		}

		public SnipeLogicNode GetNodeById(int id)
		{
			if (Nodes != null && Nodes.TryGetValue(id, out var node))
			{
				return node;
			}

			return null;
		}
		
		public SnipeLogicNode GetNodeByTreeId(int id)
		{
			if (Nodes != null)
			{
				foreach (var node in Nodes.Values)
				{
					if (node?.tree != null && node.tree.id == id)
					{
						return node;
					}
				}
			}

			return null;
		}

		public SnipeLogicNode GetNodeByName(string name)
		{
			if (Nodes != null)
			{
				foreach (var node in Nodes.Values)
				{
					if (string.Equals(node?.name, name, StringComparison.Ordinal))
					{
						return node;
					}
				}
			}

			return null;
		}
		
		public SnipeLogicNode GetNodeByTreeStringID(string stringID)
		{
			if (Nodes != null)
			{
				foreach (var node in Nodes.Values)
				{
					if (string.Equals(node?.tree?.stringID, stringID, StringComparison.Ordinal))
					{
						return node;
					}
				}
			}

			return null;
		}

		public SnipeLogicNode GetNodeByTag(string tag)
		{
			//if (Nodes != null)
			//{
			//	foreach (var node in Nodes.Values)
			//	{
			//		if (node != null && node.tree.tags.Contains(tag))
			//		{
			//			return node;
			//		}
			//	}
			//}

			if (mTaggedNodes != null && mTaggedNodes.TryGetValue(tag, out var node))
			{
				return node;
			}

			return null;
		}

		//public SnipeLogicNode FindNodeByProductSku(string sku)
		//{
		//	if (Nodes != null)
		//	{
		//		foreach (var node in Nodes.Values)
		//		{
		//			if (node != null && node.PurchaseProductSku == sku)
		//			{
		//				return node;
		//			}
		//		}
		//	}

		//	return null;
		//}

		public void RequestLogicGet(bool force = false)
		{
			if (mUpdateRequested)
				return;

			if (mLogicTable == null)
				return;

			float current_time = UnityEngine.Time.realtimeSinceStartup;
			if (!force && mUpdateRequestedTime > 0.0f && current_time - mUpdateRequestedTime < UpdateMinTimeout)
				return;

			mUpdateRequestedTime = current_time;
			mUpdateRequested = true;

			var request = SnipeApiBase.CreateRequest("logic.get", new SnipeObject() { ["noDump"] = false });
			request?.Request();
		}

		public void IncVar(SnipeLogicNode node, string name)
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

			mUpdateRequestedTime = 0.0f; // reset timer
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
			}
		}

		private void OnLogicGet(string error_code, SnipeObject response_data)
		{
			mUpdateRequested = false;
			
			if (mLogicTable == null || response_data == null)
				return;

			if (error_code == "ok")
			{
				var logic_nodes = new List<SnipeLogicNode>();
				if (response_data["logic"] is IList src_logic)
				{
					foreach (SnipeObject o in src_logic)
					{
						if (o?["node"] is SnipeObject node)
							logic_nodes.Add(new SnipeLogicNode(node, mLogicTable));
					}
				}
				
				bool timer_finished = false;

				if (Nodes == null)
				{
					Nodes = new Dictionary<int, SnipeLogicNode>();
					mTaggedNodes = new Dictionary<string, SnipeLogicNode>();
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
					mTaggedNodes.Clear();

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

				// Because of rounding sometimes messages may contain zero timer values.
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
		
		private void AddTaggedNode(SnipeLogicNode node)
		{
			if (node?.tree?.tags != null)
			{
				foreach (string tag in node.tree.tags)
				{
					if (!string.IsNullOrEmpty(tag))
					{
						mTaggedNodes[tag] = node;
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
			if (mSecondsTimer == null)
				mSecondsTimer = new Timer(OnSecondsTimerTick, null, 0, 1000);
		}

		private void StopSecondsTimer()
		{
			if (mSecondsTimer == null)
				return;

			mSecondsTimer.Dispose();
			mSecondsTimer = null;
		}

		private void OnSecondsTimerTick(object state = null)
		{
			if (Nodes != null)
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