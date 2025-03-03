
//  JSON support

using System.Collections.Generic;

namespace MiniIT
{
	public static class SnipeObjectFastJsonExtensions
	{
		public static string ToFastJSONString(this IDictionary<string, object> dictionary)
		{
			return fastJSON.JSON.ToJSON(dictionary);
		}
	}

	public partial class SnipeObject
	{
		public static SnipeObject FromFastJSONString(string input)
		{
			var decoded = fastJSON.JSON.Parse(input);
			if (decoded is Dictionary<string, object> dict)
				return new SnipeObject(dict);
			else
				return new SnipeObject() { ["data"] = decoded };
		}
	}
}
