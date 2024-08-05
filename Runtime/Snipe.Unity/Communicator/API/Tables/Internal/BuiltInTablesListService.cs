using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Threading.Tasks;
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
		public IReadOnlyList<BuiltInTablesListItem> Items => _items;
		private List<BuiltInTablesListItem> _items;

		public async AlterTask InitializeAsync(CancellationToken cancellationToken = default)
		{
			_items = await ReadBuiltInTablesVersions(cancellationToken);
		}

		public bool TryGetTableVersion(string name, out long version)
		{
			if (_items == null)
			{
				version = 0;

				var logger = SnipeServices.LogService.GetLogger(nameof(BuiltInTablesListService));
				logger.LogError($"Not initialized. Call {nameof(InitializeAsync)} first");

				return false;
			}

			foreach (var item in _items)
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

		private async AlterTask<List<BuiltInTablesListItem>> ReadBuiltInTablesVersions(CancellationToken cancellationToken)
		{
			var result = new List<BuiltInTablesListItem>();

			string json = await StreamingAssetsReader.ReadTextAsync("snipe_tables.json", cancellationToken);
			if (string.IsNullOrEmpty(json))
			{
				SnipeServices.LogService.GetLogger(nameof(BuiltInTablesListService)).LogError("ReadBuiltInTablesVersions failed to read snipe_tables.json");
				return result;
			}

			var wrapper = SnipeObject.FromFastJSONString(json);
			if (wrapper?["tables"] is IList list)
			{
				foreach (var loadedItem in list)
				{
					if (loadedItem is IDictionary<string, object> item)
					{
						var resultItem = new BuiltInTablesListItem()
						{
							name = SnipeObject.SafeGetString(item, "name"),
							version = SnipeObject.SafeGetValue<long>(item, "version"),
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
