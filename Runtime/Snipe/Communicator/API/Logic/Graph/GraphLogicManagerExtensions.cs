namespace MiniIT.Snipe.Api
{
	public static class GraphLogicManagerExtensions
	{
		public static void IncGraphVar(this GraphLogicManager manager, int graphID, string name, int delta = 1)
		{
			manager.ChangeGraphVar(graphID, name, delta);
		}

		public static void DecGraphVar(this GraphLogicManager manager, int graphID, string name, int delta = 1)
		{
			manager.ChangeGraphVar(graphID, name, -delta);
		}
	}
}
