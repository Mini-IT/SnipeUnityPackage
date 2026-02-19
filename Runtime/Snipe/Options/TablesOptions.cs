
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class TablesOptions
	{
		public enum VersionsResolution
		{
			Default,
			ForceBuiltIn,
			ForceExternal,
		}

		public VersionsResolution Versioning = VersionsResolution.Default;

		public IReadOnlyList<string> TablesUrls => _tablesUrls;

		private readonly List<string> _tablesUrls = new List<string>();
		private int _tablesUrlIndex = -1;

		public void ResetTablesUrls()
		{
			_tablesUrls.Clear();
			_tablesUrlIndex = -1;
		}

		public void AddTableUrl(string url)
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

		public void Init(IDictionary<string, object> data)
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

		public string GetTablesPath(bool next = false)
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
