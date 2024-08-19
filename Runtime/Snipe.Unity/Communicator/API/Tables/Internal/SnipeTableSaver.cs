using System;
using System.IO;
using Microsoft.Extensions.Logging;
using MiniIT.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableSaver
	{
		public static async AlterTask SaveToCacheAsync(Stream stream, string table_name, long version)
		{
			string cache_path = TablesLoader.GetCachePath(table_name, version);

			try
			{
				if (!File.Exists(cache_path))
				{
					SnipeServices.LogService.GetLogger("SnipeTable").LogTrace($"Save to cache {cache_path}");

					string directory_path = TablesLoader.GetCacheDirectoryPath();
					if (!Directory.Exists(directory_path))
					{
						Directory.CreateDirectory(directory_path);
					}

					using (FileStream cache_write_stream = new FileStream(cache_path, FileMode.Create, FileAccess.Write))
					{
#if UNITY_WEBGL
						stream.CopyTo(cache_write_stream);
						await AlterTask.CompletedTask;
#else
						await stream.CopyToAsync(cache_write_stream).ConfigureAwait(false);
#endif
					}
				}
			}
			catch (Exception e)
			{
				SnipeServices.LogService.GetLogger("SnipeTable").LogTrace($"Failed to save to cache - {table_name} - {e}");

				if (File.Exists(cache_path))
				{
					File.Delete(cache_path);
				}
			}
		}
	}
}
