using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Unity;

namespace MiniIT.Snipe.Tables
{
	public struct BuiltInTablesListItem
	{
		public string name;
		public long version;
	}

	public class BuiltInTablesListService
	{
		public List<BuiltInTablesListItem> Items { get; private set; }

		public async Task InitializeAsync(CancellationToken cancellationToken = default)
		{
			Items = await ReadBuiltInTablesVersions(cancellationToken);
		}

		public bool TryGetTableVersion(string name, out long version)
		{
			if (Items == null)
			{
				version = 0;

				var logger = SnipeServices.LogService.GetLogger(nameof(BuiltInTablesListService));
				logger.LogError($"Not initialized. Call {nameof(InitializeAsync)} first");

				return false;
			}

			foreach (var item in Items)
			{
				if (item.name == name)
				{
					version = item.version;
					return true;
				}
			}

			version = 0;
			return false;
		}

		private async Task<List<BuiltInTablesListItem>> ReadBuiltInTablesVersions(CancellationToken cancellationToken)
		{
			var result = new List<BuiltInTablesListItem>();

			string json = await StreamingAssetsReader.ReadTextAsync("snipe_tables.json", cancellationToken);
			if (json == null)
			{
				SnipeServices.LogService.GetLogger(nameof(BuiltInTablesListService)).LogError("ReadBuiltInTablesVersions failed to load or parse snipe_tables.json");
				return result;
			}

			var wrapper = SnipeObject.FromFastJSONString(json);
			if (wrapper != null && wrapper["tables"] is IList list)
			{
				foreach (var loadedItem in list)
				{
					if (loadedItem is SnipeObject item)
					{
						var resultItem = new BuiltInTablesListItem()
						{
							name = item.SafeGetString("name"),
							version = item.SafeGetValue<long>("version"),
						};

						if (resultItem.version != 0 && !string.IsNullOrEmpty(resultItem.name))
						{
							result.Add(resultItem);
						}
					}
				}
			}

			return result;
		}
	}
}
