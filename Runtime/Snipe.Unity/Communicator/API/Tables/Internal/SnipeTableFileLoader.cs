using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableFileLoader
	{
		public static async UniTask<bool> LoadAsync(ISnipeServices services, Type wrapperType, IDictionary items, string tableName, long version)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			bool loaded = false;
			string file_path = GetFilePath(services, tableName, version);

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
						services.LoggerFactory.CreateLogger("SnipeTable").LogTrace("Table ready (from cache) - {tableName}", tableName);
					}
					else
					{
						services.LoggerFactory.CreateLogger("SnipeTable").LogTrace("Failed to read file - {tableName}", tableName);
					}
				}
			}

			return loaded;
		}

		private static string GetFilePath(ISnipeServices services, string tableName, long version)
		{
			if (version <= 0 && Directory.Exists(TablesLoader.GetCacheDirectoryPath(services)))
			{
				var files = Directory.EnumerateFiles(TablesLoader.GetCacheDirectoryPath(services), $"*{tableName}.json.gz");
				foreach (var file in files)
				{
					return file;
				}
			}

			return TablesLoader.GetCachePath(services, tableName, version);
		}
	}
}
