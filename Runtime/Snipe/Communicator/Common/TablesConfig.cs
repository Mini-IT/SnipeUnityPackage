
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class TablesConfig
	{
		public enum VersionsResolution
		{
			Default,
			ForceBuiltIn,
			ForceExternal,
		}

		public static VersionsResolution Versioning = VersionsResolution.Default;

		public static List<string> TablesUrls = new List<string>();
		private static int _tablesUrlIndex = 0;

		public static void Init(IDictionary<string, object> data)
		{
			if (TablesUrls == null)
				TablesUrls = new List<string>();
			else
				TablesUrls.Clear();

			if (data.TryGetValue("tables_path", out var tables_path_field) &&
				tables_path_field is IList tables_ulrs_list)
			{
				foreach (string path in tables_ulrs_list)
				{
					var corrected_path = path.Trim();
					if (!corrected_path.EndsWith("/"))
						corrected_path += "/";

					TablesUrls.Add(corrected_path);
				}
			}

			_tablesUrlIndex = -1;
		}

		public static string GetTablesPath(bool next = false)
		{
			_tablesUrlIndex = SnipeConfig.GetValidIndex(TablesUrls, _tablesUrlIndex, next);
			if (_tablesUrlIndex >= 0)
			{
				return TablesUrls[_tablesUrlIndex];
			}

			return null;
		}
	}
}