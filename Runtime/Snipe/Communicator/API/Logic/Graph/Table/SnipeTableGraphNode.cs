using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeTableGraphNode
	{
		public int id;
		public string type;
		public string note;
		public string data;
		public List<int> inputs;
		public List<int> outputs;
	}
}
