using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableFileLoader
	{
		public static async UniTask<bool> LoadAsync(Type wrapperType, IDictionary items, string tableName, long version)
		{
			bool loaded = false;
			string file_path = GetFilePath(tableName, version);

			if (!string.IsNullOrEmpty(file_path) && File.Exists(file_path))
			{
				using (var read_stream = new FileStream(file_path, FileMode.Open))
				{
#if UNITY_WEBGL
					if (SnipeTableGZipReader.TryRead(wrapperType, items, read_stream))
#else
					if (await SnipeTableGZipReader.TryReadAsync(wrapperType, items, read_stream))
#endif
					{
						loaded = true;
						SnipeServices.Instance.LogService.GetLogger("SnipeTable").LogTrace("Table ready (from cache) - {tableName}", tableName);
					}
					else
					{
						SnipeServices.Instance.LogService.GetLogger("SnipeTable").LogTrace("Failed to read file - {tableName}", tableName);
					}
				}
			}

			return loaded;
		}

		private static string GetFilePath(string tableName, long version)
		{
			if (version <= 0 && Directory.Exists(TablesLoader.GetCacheDirectoryPath()))
			{
				var files = Directory.EnumerateFiles(TablesLoader.GetCacheDirectoryPath(), $"*{tableName}.json.gz");
				foreach (var file in files)
				{
					return file;
				}
			}

			return TablesLoader.GetCachePath(tableName, version);
		}
	}
}
