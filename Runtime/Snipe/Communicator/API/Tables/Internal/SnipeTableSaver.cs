using System;
using System.IO;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableSaver
	{
		public static void SaveToCache(Stream stream, string table_name, long version)
		{
			SaveToCacheAsync(stream, table_name, version);
		}

		private static async void SaveToCacheAsync(Stream stream, string table_name, long version)
		{
			string cache_path = TablesLoader.GetCachePath(table_name, version);

			try
			{
				if (!File.Exists(cache_path))
				{
					SnipeServices.Instance.LogService.GetLogger("SnipeTable").LogTrace($"Save to cache {cache_path}");

					string directory_path = TablesLoader.GetCacheDirectoryPath();
					if (!Directory.Exists(directory_path))
					{
						Directory.CreateDirectory(directory_path);
					}

					using (FileStream cache_write_stream = new FileStream(cache_path, FileMode.Create, FileAccess.Write))
					{
						await stream.CopyToAsync(cache_write_stream).ConfigureAwait(false);
					}
				}
			}
			catch (Exception e)
			{
				SnipeServices.Instance.LogService.GetLogger("SnipeTable").LogTrace($"Failed to save to cache - {table_name} - {e}");

				if (File.Exists(cache_path))
				{
					File.Delete(cache_path);
				}
			}
		}
	}
}
