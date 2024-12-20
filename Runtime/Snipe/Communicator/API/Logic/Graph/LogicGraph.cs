namespace MiniIT.Snipe.Api
{
	public struct LogicGraph
	{
		public int ID { get; }
		public SnipeTableGraphsItem TableItem { get; }
		public LogicGraphState State { get; }

		public LogicGraph(int id, SnipeTableGraphsItem tableItem, LogicGraphState state)
		{
			ID = id;
			TableItem = tableItem;
			State = state;
		}
	}
}
