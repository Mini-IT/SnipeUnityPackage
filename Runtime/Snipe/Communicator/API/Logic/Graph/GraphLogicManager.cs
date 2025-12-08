using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

using GraphUpdatedDelegate = System.Action<System.Collections.Generic.Dictionary<int, MiniIT.Snipe.Api.LogicGraph>>;

namespace MiniIT.Snipe.Api
{
	public class GraphLogicManager : AbstractSnipeApiModuleManagerWithTable
	{
		public event GraphUpdatedDelegate GraphUpdated;
		public event Action<LogicGraph> GraphFinished;
		public event Action<LogicGraph> GraphAborted;
		public event Action<LogicGraph> GraphRestarted;

		public Dictionary<int, LogicGraph> Graphs { get; } = new Dictionary<int, LogicGraph>();

		private List<GraphUpdatedDelegate> _graphGetCallbacks;

		private readonly LogicGraphParser _graphParser;
		private readonly ILogger _logger;

		public GraphLogicManager(ISnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory,
			ISnipeTable<SnipeTableGraphsItem> table)
			: base(communicator, requestFactory, table)
		{
			_graphParser = new LogicGraphParser(table);
			_logger = SnipeServices.Instance.LogService.GetLogger<GraphLogicManager>();
		}

		~GraphLogicManager()
		{
			Dispose();
		}

		public override void Dispose()
		{
			Graphs.Clear();

			base.Dispose();

			GC.SuppressFinalize(this);
		}

		public void RequestGraphGet(GraphUpdatedDelegate callback = null)
		{
			if (callback != null)
			{
				_graphGetCallbacks ??= new List<GraphUpdatedDelegate>();
				_graphGetCallbacks.Add(callback);
			}

			var request = _requestFactory.Invoke("graph.get");
			request?.Request();
		}

		public bool TryGetGraphAndVar(int graphID, string varName, out LogicGraph graph, out object graphVar)
		{
			if (!Graphs.TryGetValue(graphID, out graph) || graph.ID <= 0 || graph.State.NodeID <= 0)
			{
				_logger.LogError($"No graph with ID {graphID} is found");
				graphVar = null;
				return false;
			}

			if (!graph.State.Vars.TryGetValue(varName, out object boxedValue))
			{
				graphVar = null;
				return false;
			}

			graphVar = boxedValue;
			return true;
		}

		public void SetGraphVar(int graphID, string name, object value)
		{
			if (!TryGetGraphAndVar(graphID, name, out LogicGraph graph, out object currentValue))
			{
				_logger.LogError($"Graph with ID {graphID} has no variable named {name}");
				return;
			}

			if (currentValue.Equals(value))
			{
				return;
			}

			graph.State.Vars[name] = value;

			var request = _requestFactory.Invoke("graph.set",
				new Dictionary<string, object>()
				{
					["graphID"] = graphID,
					["name"] = name,
					["val"] = value,
				});
			request?.Request();
		}

		public void ChangeGraphVar(int graphID, string name, int delta)
		{
			if (!TryGetGraphAndVar(graphID, name, out LogicGraph graph, out object boxedValue))
			{
				_logger.LogError($"Graph with ID {graphID} has no variable named {name}");
				return;
			}

			int currentValue = Convert.ToInt32(boxedValue);
			graph.State.Vars[name] = currentValue + delta;

			var request = _requestFactory.Invoke("graph.change",
				new Dictionary<string, object>()
				{
					["graphID"] = graphID,
					["name"] = name,
					["val"] = delta,
				});
			request?.Request();
		}

		protected override void OnSnipeMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
		{
			switch (messageType)
			{
				case "graph.get":
					OnGraphGet(errorCode, data);

					if (_graphGetCallbacks != null)
					{
						foreach (var cb in _graphGetCallbacks)
						{
							cb?.Invoke(Graphs);
						}
						_graphGetCallbacks.Clear();
					}
					break;

				case "graph.set":
				case "graph.change":
					RequestGraphGet();
					break;

				case "graph.aborted":
					ProcessGraphEnded(errorCode, data, GraphAborted);
					break;

				case "graph.restarted":
					ProcessGraphEnded(errorCode, data, GraphRestarted);
					break;

				case "graph.finished":
					ProcessGraphEnded(errorCode, data, GraphFinished);
					break;
			}
		}

		private void OnGraphGet(string errorCode, IDictionary<string, object> responseData)
		{
			if (responseData == null || errorCode != "ok")
			{
				return;
			}

			if (!responseData.TryGetValue("list", out var list) || list is not IList graphsList)
			{
				return;
			}

			Graphs.Clear();

			foreach (var item in graphsList)
			{
				if (item is IDictionary<string, object> so && _graphParser.TryParse(so, out var graph))
				{
					Graphs[graph.ID] = graph;
				}
			}

			GraphUpdated?.Invoke(Graphs);
		}

		private void ProcessGraphEnded(string errorCode, IDictionary<string, object> data, Action<LogicGraph> callback)
		{
			if (data == null || errorCode != "ok")
			{
				return;
			}

			if (Graphs.TryGetValue(data.SafeGetValue("id", 0), out LogicGraph graph))
			{
				callback?.Invoke(graph);
			}

			RequestGraphGet();
		}
	}
}
