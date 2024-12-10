using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class GraphLogicManager : AbstractSnipeApiModuleManager
	{
		public delegate void GraphUpdatedHandler(Dictionary<int, LogicGraph> nodes);
		public delegate void GraphFinishedHandler(LogicGraph graph);
		// public delegate void NodeProgressHandler(GraphLogicNode node, SnipeLogicNodeVar nodeVar, int oldValue);

		public event GraphUpdatedHandler GraphLogicUpdated;
		public event GraphFinishedHandler GraphFinished;
		// public event NodeProgressHandler NodeProgress;

		public Dictionary<int, LogicGraph> Graphs { get; } = new Dictionary<int, LogicGraph>();

		public GraphLogicManager(SnipeCommunicator communicator,
			AbstractSnipeApiService.RequestFactoryMethod requestFactory)
			: base(communicator, requestFactory)
		{
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

		public void RequestGraphGet(bool force = false)
		{
			var request = _requestFactory.Invoke("graph.get");
			request?.Request();
		}

		public void SetGraphVar(int graphID, string name, object value)
		{
			if (!Graphs.TryGetValue(graphID, out LogicGraph graph) || graph?.State == null)
			{
				return;
			}

			if (!graph.State.Vars.TryGetValue(name, out var currentValue) || currentValue.Equals(value))
			{
				return;
			}

			var request = _requestFactory.Invoke("graph.change",
				new SnipeObject()
				{
					["graphID"] = graphID,
					["name"] = name,
					["val"] = value,
				});
			request?.Request();
		}

		protected override void OnSnipeMessageReceived(string messageType, string errorCode, SnipeObject data, int requestId)
		{
			switch (messageType)
			{
				case "graph.get":
					OnGraphGet(messageType, data);
					break;

				case "graph.change":
					//RequestLogicGet(true);
					break;

				case "graph.finish":
				case "graph.aborted":
				case "graph.finished":
				case "graph.restarted":
					break;
			}
		}

		private void OnGraphGet(string errorCode, SnipeObject responseData)
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
				if (item is SnipeObject so)
				{
					var graph = new LogicGraph(so);
					Graphs[graph.ID] = graph;
				}
			}
		}

		private void OnLogicExitNode(string errorCode, SnipeObject data)
		{
			LogicGraph graph = Graphs.GetValueOrDefault(data.SafeGetValue("id", 0));
			GraphFinished?.Invoke(graph);

			// RequestLogicGet(true);
		}
	}
}
