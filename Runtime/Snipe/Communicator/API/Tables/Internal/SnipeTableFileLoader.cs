using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableFileLoader
	{
		public static async Task<bool> LoadAsync(Type wrapperType, IDictionary items, string table_name, long version)
		{
			bool loaded = false;
			string file_path = GetFilePath(table_name, version);
			
			if (!string.IsNullOrEmpty(file_path) && File.Exists(file_path))
			{
				using (var read_stream = new FileStream(file_path, FileMode.Open))
				{
					if (await SnipeTableGZipReader.TryReadAsync(wrapperType, items, read_stream))
					{
						loaded = true;
						SnipeServices.Instance.LogService.GetLogger("SnipeTable").LogTrace($"Table ready (from cache) - {table_name}");
					}
					else
					{
						SnipeServices.Instance.LogService.GetLogger("SnipeTable").LogTrace($"Failed to read file - {table_name}");
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
