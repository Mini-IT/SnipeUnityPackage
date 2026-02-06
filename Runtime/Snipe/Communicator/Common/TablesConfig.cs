
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public static class TablesConfig
	{
		public enum VersionsResolution
		{
			Default,
			ForceBuiltIn,
			ForceExternal,
		}

		public static VersionsResolution Versioning = VersionsResolution.Default;

		public static IReadOnlyList<string> TablesUrls => _tablesUrls;

		private static List<string> _tablesUrls = new List<string>();
		private static int _tablesUrlIndex = -1;

		public static void ResetTablesUrls()
		{
			_tablesUrls.Clear();
			_tablesUrlIndex = -1;
		}

		public static void AddTableUrl(string url)
		{
			url = url.Trim();
			if (string.IsNullOrEmpty(url))
			{
				return;
			}

			if (url[url.Length - 1] != '/') // faster than string.EndsWith("/")
			{
				url += "/";
			}

			_tablesUrls.Add(url);
		}

		public static void Init(IDictionary<string, object> data)
		{
			ResetTablesUrls();

			if (data.TryGetValue("tables_path", out var tablesPathField) &&
				tablesPathField is IList tablesUlrsList)
			{
				foreach (string path in tablesUlrsList)
				{
					string correctedPath = path.Trim();
					if (string.IsNullOrEmpty(correctedPath))
					{
						continue;
					}

					if (correctedPath[correctedPath.Length - 1] != '/') // faster than string.EndsWith("/")
					{
						correctedPath += "/";
					}

					_tablesUrls.Add(correctedPath);
				}
			}

			_tablesUrlIndex = -1;
		}

		public static string GetTablesPath(bool next = false)
		{
			_tablesUrlIndex = SnipeOptions.GetValidIndex(_tablesUrls, _tablesUrlIndex, next);
			if (_tablesUrlIndex >= 0)
			{
				return _tablesUrls[_tablesUrlIndex];
			}

			return null;
		}
	}
}
