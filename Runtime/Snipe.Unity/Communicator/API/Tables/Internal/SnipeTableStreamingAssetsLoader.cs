using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Unity;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableStreamingAssetsLoader
	{
		private readonly BuiltInTablesListService _builtInTablesListService;
		private readonly ILogger _logger;

		public SnipeTableStreamingAssetsLoader(BuiltInTablesListService builtInTablesListService)
		{
			_builtInTablesListService = builtInTablesListService;
			_logger = SnipeServices.LogService.GetLogger("SnipeTable");
		}

		public async UniTask<bool> LoadAsync(Type wrapperType, IDictionary items, string tableName, long version, CancellationToken cancellationToken = default)
		{
			_logger.LogTrace("ReadFromStramingAssets - {tableName}", tableName);

			string filePath = GetFilePath(tableName, version);

			byte[] data = await StreamingAssetsReader.ReadBytesAsync(filePath, cancellationToken);

			if (data == null || data.Length == 0)
			{
				_logger.LogTrace("Failed to read file {filePath}", filePath);
				return false;
			}

			bool loaded = false;

			using (var readStream = new MemoryStream(data))
			{
				try
				{
#if UNITY_WEBGL
					SnipeTableGZipReader.Read(wrapperType, items, readStream);
#else
					// TODO: use cancellationToken
					await SnipeTableGZipReader.ReadAsync(wrapperType, items, readStream);
#endif
					loaded = true;
				}
				catch (Exception e)
				{
					_logger.LogTrace("Failed to read file - {tableName} - {e}", tableName, e);
				}
			}

			if (loaded)
			{
				_logger.LogTrace("Table ready (built-in) - {tableName}", tableName);
			}

			return loaded;
		}

		private string GetFilePath(string tableName, long version)
		{
			// NOTE: There is a bug - only lowercase works
			// (https://issuetracker.unity3d.com/issues/android-loading-assets-from-assetbundles-takes-significantly-more-time-when-the-project-is-built-as-an-aab)
			tableName = tableName.ToLower();

			if (version <= 0)
			{
				_builtInTablesListService.TryGetTableVersion(tableName, out version);
			}

			return $"{version}_{tableName}.jsongz";
		}
	}
}
