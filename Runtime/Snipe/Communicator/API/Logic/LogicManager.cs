using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class LogicManager : IDisposable
	{
		public static TimeSpan UpdateMinTimeout = TimeSpan.FromSeconds(30);

		public delegate void LogicUpdatedHandler(Dictionary<int, LogicNode> nodes);
		public delegate void ExitNodeHandler(LogicNode node, List<object> results);
		public delegate void NodeProgressHandler(LogicNode node, SnipeLogicNodeVar nodeVar, int oldValue);

		public event LogicUpdatedHandler LogicUpdated;
		public event ExitNodeHandler ExitNode;
		public event NodeProgressHandler NodeProgress;

		class QueuedMessage
		{
			internal string messageType;
			internal string errorCode;
			internal SnipeObject data;
		}

		public Dictionary<int, LogicNode> Nodes { get; private set; } = new Dictionary<int, LogicNode>();
		private Dictionary<string, LogicNode> _taggedNodes;
		private SnipeTable<SnipeTableLogicItem> _logicTable = null;

		private SnipeCommunicator _snipeCommunicator;
		private readonly AbstractSnipeApiService.RequestFactoryMethod _requestFactory;
		private TimeSpan _updateRequestedTime = TimeSpan.Zero;
		private SnipeObject _logicGetRequestParameters;
		
		private Stopwatch _refTime = Stopwatch.StartNew();
		private Timer _secondsTimer;

		private CancellationTokenSource _waitingTableCancellation;
		private Queue<QueuedMessage> _waitingTableMessages;

		public LogicManager(SnipeCommunicator communicator, AbstractSnipeApiService.RequestFactoryMethod requestFactory, SnipeTable<SnipeTableLogicItem> logicTable)
		{
			_requestFactory = requestFactory;
			_logicTable = logicTable;

			ClearCommunicatorReference();

			_snipeCommunicator = communicator;

			if (_logicTable != null)
			{
				_snipeCommunicator.MessageReceived += OnSnipeMessageReceived;
				_snipeCommunicator.PreDestroy += OnSnipeCommunicatorPreDestroy;

				if (!_logicTable.Loaded)
				{
					_waitingTableCancellation = new CancellationTokenSource();
					WaitForTableLoaded(_waitingTableCancellation.Token);
				}
			}
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
			if (_waitingTableCancellation != null)
			{
				_waitingTableCancellation.Cancel();
				_waitingTableCancellation = null;
			}

			StopSecondsTimer();

			ClearCommunicatorReference();

			_logicTable = null;
			Nodes.Clear();
			_taggedNodes = null;
		}

		private void ClearCommunicatorReference()
		{
			if (_snipeCommunicator != null)
			{
				_snipeCommunicator.MessageReceived -= OnSnipeMessageReceived;
				_snipeCommunicator.PreDestroy -= OnSnipeCommunicatorPreDestroy;
				_snipeCommunicator = null;
			}
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

			var request = _requestFactory.Invoke("logic.get", _logicGetRequestParameters);
			request?.Request();
		}

		public void IncVar(LogicNode node, string name)
		{
			IncVar(name, node?.tree?.id ?? 0);
		}

		public void IncVar(string name, int tree_id = 0) //, int amount = 0)
		{
			SnipeObject requestData = new SnipeObject()
			{
				["name"] = name,
			};
			if (tree_id != 0)
				requestData["treeID"] = tree_id;
			//if (amount != 0)
			//	request_data["amount"] = amount;
			var request = _requestFactory.Invoke("logic.incVar", requestData);
			if (request != null)
			{
				request.Request();
			}

			_updateRequestedTime = TimeSpan.Zero; // reset timer
		}

		private void OnSnipeMessageReceived(string messageType, string errorCode, SnipeObject data, int requestId)
		{
			switch (messageType)
			{
				case "logic.get":
					ProcessMessage(messageType, errorCode, data, OnLogicGet);
					break;

				case "logic.exitNode":
					ProcessMessage(messageType, errorCode, data, OnLogicExitNode);
					break;

				case "logic.progress":
					ProcessMessage(messageType, errorCode, data, OnLogicProgress);
					break;

				case "logic.incVar":
					ProcessMessage(messageType, errorCode, data, (errorCode, responseData) => RequestLogicGet(true));
					break;
			}
		}

		private void ProcessMessage(string messageType, string errorCode, SnipeObject data, Action<string, SnipeObject> handler)
		{
			if (_logicTable.Loaded)
			{
				handler.Invoke(errorCode, data);
				return;
			}

			_waitingTableMessages ??= new Queue<QueuedMessage>();
			_waitingTableMessages.Enqueue(new QueuedMessage() { messageType = messageType, errorCode = errorCode, data = data });
		}

		private void OnLogicGet(string errorCode, SnipeObject responseData)
		{
			_updateRequestedTime = TimeSpan.Zero; // reset timer  // ????????

			if (responseData == null || errorCode != "ok")
			{
				return;
			}

			var logicNodes = new List<LogicNode>();
			if (responseData["logic"] is IList srcLogic)
			{
				foreach (object o in srcLogic)
				{
					if (o is SnipeObject so && so?["node"] is SnipeObject node)
					{
						logicNodes.Add(new LogicNode(node, _logicTable));
					}
				}
			}
				
			bool timerFinished = false;

			if (Nodes.Count == 0)
			{
				_taggedNodes = new Dictionary<string, LogicNode>();
				foreach (LogicNode node in logicNodes)
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

				foreach (var node in logicNodes)
				{
					if (node != null && node.id > 0)
					{
						if (node.timeleft == 0) // (-1) means that the node does not have a timer
						{
							timerFinished = true;
						}

						if (Nodes.TryGetValue(node.id, out var storedNode))
						{
							storedNode.CopyVars(node);
							AddTaggedNode(storedNode);
						}
						else
						{
							Nodes.Add(node.id, node);
							AddTaggedNode(node);
						}
					}
				}

				List<int> nodesToExclude = null;
				foreach (var storedNodeId in Nodes.Keys)
				{
					bool found = false;
					foreach (var node in logicNodes)
					{
						if (node != null && node.id == storedNodeId)
						{
							found = true;
							break;
						}
					}
					if (!found)
					{
						nodesToExclude ??= new List<int>();
						nodesToExclude.Add(storedNodeId);
					}
				}

				if (nodesToExclude != null)
				{
					foreach (int key in nodesToExclude)
					{
						Nodes.Remove(key);
					}
				}
			}

			// Highly unlikely but sometimes
			// messages may contain zero timer values (because of rounding).
			// In this case just request once more
			if (timerFinished)
			{
				RequestLogicGet(true);
			}
			else
			{
				LogicUpdated?.Invoke(Nodes);
				StartSecondsTimer();
			}
		}
		
		private void AddTaggedNode(LogicNode node)
		{
			if (node?.tree?.tags == null)
				return;

			foreach (string tag in node.tree.tags)
			{
				if (!string.IsNullOrEmpty(tag))
				{
					_taggedNodes[tag] = node;
				}
			}
		}

		private void OnLogicExitNode(string errorCode, SnipeObject data)
		{
			LogicNode node = GetNodeById(data.SafeGetValue("id", 0));
			ExitNode?.Invoke(node, data["results"] as List<object>);

			RequestLogicGet(true);
		}

		private void OnLogicProgress(string errorCode, SnipeObject data)
		{
			//Progress?.Invoke(data.SafeGetValue<int>("id"),
			//			data.SafeGetValue<int>("treeID"),
			//			data.SafeGetValue<int>("oldValue"),
			//			data.SafeGetValue<int>("value"),
			//			data.SafeGetValue<int>("maxValue"));

			LogicNode node = GetNodeById(data.SafeGetValue("id", 0));
			if (node == null)
				return;

			string varName = data.SafeGetString("name");
			if (string.IsNullOrEmpty(varName))
				return;

			SnipeLogicNodeVar nodeVar = null;
			foreach (SnipeLogicNodeVar v in node.vars)
			{
				if (string.Equals(v.name, varName, StringComparison.Ordinal))
				{
					nodeVar = v;
					break;
				}
			}

			if (nodeVar != null)
			{
				nodeVar.value = data.SafeGetValue<int>("value");

				int oldValue = data.SafeGetValue<int>("oldValue");
				NodeProgress?.Invoke(node, nodeVar, oldValue);
			}
		}

		private async void WaitForTableLoaded(CancellationToken cancellation)
		{
			while (_logicTable != null && !_logicTable.Loaded)
			{
				try
				{
					await Task.Delay(100, cancellation);
				}
				catch (OperationCanceledException)
				{
					return;
				}

				if (cancellation.IsCancellationRequested)
				{
					return;
				}
			}

			if (_waitingTableMessages != null)
			{
				while (_waitingTableMessages.Count > 0)
				{
					QueuedMessage message = _waitingTableMessages.Dequeue();
					OnSnipeMessageReceived(message.messageType, message.errorCode, message.data, 0);
				}
			}
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
