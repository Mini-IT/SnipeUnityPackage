using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableFileLoader
	{
		public static async Task<bool> LoadAsync<TItem, TWrapper>(Dictionary<int, TItem> items, string table_name, long version)
			where TItem : SnipeTableItem, new()
			where TWrapper : class, ISnipeTableItemsListWrapper<TItem>, new()
		{
			bool loaded = false;
			string file_path = GetFilePath(table_name, version);
			
			if (!string.IsNullOrEmpty(file_path) && File.Exists(file_path))
			{
				using (var read_stream = new FileStream(file_path, FileMode.Open))
				{
					if (await SnipeTableGZipParser.TryReadAsync<TItem, TWrapper>(items, read_stream))
					{
						loaded = true;
						DebugLogger.Log($"[SnipeTable] Table ready (from cache) - {table_name}");
					}
					else
					{
						DebugLogger.Log($"[SnipeTable] Failed to read file - {table_name}");
					}
				}
			}

			return loaded;
		}
		
		private static string GetFilePath(string table_name, long version)
		{
			if (version <= 0 && Directory.Exists(TablesLoader.GetCacheDirectoryPath()))
			{
				var files = Directory.EnumerateFiles(TablesLoader.GetCacheDirectoryPath(), $"*{table_name}.json.gz");
				foreach (var file in files)
				{
					return file;
				}
			}
			
			return TablesLoader.GetCachePath(table_name, version);
		}
	}
}