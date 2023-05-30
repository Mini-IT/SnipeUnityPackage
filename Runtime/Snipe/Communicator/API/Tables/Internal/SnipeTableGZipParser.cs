using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableGZipParser
	{
		public static async Task<bool> TryReadAsync<TItem, TWrapper>(Dictionary<int, TItem> items, Stream stream)
			where TItem : SnipeTableItem, new()
			where TWrapper : class, ISnipeTableItemsListWrapper<TItem>, new()

		{
			try
			{
				await ReadAsync<TItem, TWrapper>(items, stream);
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		public static async Task ReadAsync<TItem, TWrapper>(Dictionary<int, TItem> items, Stream stream)
			where TItem : SnipeTableItem, new()
			where TWrapper : class, ISnipeTableItemsListWrapper<TItem>, new()
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json = await reader.ReadToEndAsync();

					TWrapper listWrapper = default;
					var wrapperType = typeof(TWrapper);

					if (wrapperType == typeof(SnipeTableLogicItemsWrapper))
					{
						DebugLogger.Log("[SnipeTable] SnipeTableLogicItemsWrapper");

						listWrapper = ParseListWrapper(json, SnipeTableLogicItemsWrapper.FromTableData) as TWrapper;
					}
					else if (wrapperType == typeof(SnipeTableCalendarItemsWrapper))
					{
						DebugLogger.Log("[SnipeTable] SnipeTableCalendarItemsWrapper");

						listWrapper = ParseListWrapper(json, SnipeTableCalendarItemsWrapper.FromTableData) as TWrapper;
					}
					else
					{
						//lock (_parseJSONLock)
						{
							listWrapper = fastJSON.JSON.ToObject<TWrapper>(json);
						}
					}

					if (listWrapper?.list != null)
					{
						foreach (TItem item in listWrapper.list)
						{
							items[item.id] = item;
						}
					}

					//this.Loaded = true;
				}
			}
		}

		private static ISnipeTableItemsListWrapper ParseListWrapper(string json, Func<Dictionary<string, object>, ISnipeTableItemsListWrapper> parser)
		{
			Dictionary<string, object> parsedData = null;
			//lock (_parseJSONLock)
			{
				parsedData = SnipeObject.FromJSONString(json);
			}

			var list_wrapper = parser.Invoke(parsedData);

			if (list_wrapper == null)
			{
				DebugLogger.Log("[SnipeTable] parsed_data is null");
			}

			return list_wrapper;
		}
	}
}
