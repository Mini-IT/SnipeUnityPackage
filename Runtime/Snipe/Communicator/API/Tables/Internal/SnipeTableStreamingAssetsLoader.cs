using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using MiniIT.Snipe.Logging;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableStreamingAssetsLoader
	{
		public static async Task<bool> LoadAsync(Type wrapperType, IDictionary items, string table_name, long version)
		{
			var logger = LogManager.GetLogger("SnipeTable");
			logger.Log($"ReadFromStramingAssets - {table_name}");

			string file_path = GetFilePath(table_name, version);

			if (!BetterStreamingAssets.FileExists(file_path))
			{
				logger.Log($"[SnipeTable] ReadFromStramingAssets - file not found: {file_path}");
				return false;
			}

			bool loaded = false;
			byte[] data = BetterStreamingAssets.ReadAllBytes(file_path);

			if (data != null)
			{
				using (var read_stream = new MemoryStream(data))
				{
					try
					{
						await SnipeTableGZipReader.ReadAsync(wrapperType, items, read_stream);
						loaded = true;
					}
					catch (Exception e)
					{
						logger.Log($"[SnipeTable] Failed to read file - {table_name} - {e}");
					}
				}

				if (loaded)
				{
					logger.Log($"[SnipeTable] Table ready (built-in) - {table_name}");
				}
			}

			return loaded;
		}

		private static string GetFilePath(string table_name, long version)
		{
			// NOTE: There is a bug - only lowercase works
			// (https://issuetracker.unity3d.com/issues/android-loading-assets-from-assetbundles-takes-significantly-more-time-when-the-project-is-built-as-an-aab)
			table_name = table_name.ToLower();

			if (version <= 0)
			{
				var files = BetterStreamingAssets.GetFiles("/", $"*{table_name}.jsongz");
				foreach (var file in files)
				{
					return file;
				}
			}

			return $"{version}_{table_name}.jsongz";
		}
	}
}
