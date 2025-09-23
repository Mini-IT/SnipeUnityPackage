using System.Collections.Generic;
using fastJSON;

namespace MiniIT
{
	public static class JsonUtility
	{
		private static JSONParameters s_jsonParameters;

		public static string ToJson(IDictionary<string, object> dictionary)
		{
			s_jsonParameters ??= new JSONParameters()
			{
				UseExtensions = false,
				UsingGlobalTypes = false,
			};

			return JSON.ToJSON(dictionary, s_jsonParameters);
		}

		public static Dictionary<string, object> ParseDictionary(string json)
		{
			var decoded = JSON.Parse(json);

			if (decoded is Dictionary<string, object> dict)
			{
				return dict;
			}

			return new Dictionary<string, object>(1) { ["data"] = decoded };
		}
	}
}
