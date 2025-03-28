using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;
using UnityEngine.Networking;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableWebLoader
	{
		private const int WEB_REQUEST_TIMEOUT_SECONDS = 3;

		private ILogger _logger;

		public async UniTask<bool> LoadAsync(Type wrapperType, IDictionary items, string tableName, long version, CancellationToken cancellation)
		{
			bool loaded = false;

			string url = GetTableUrl(tableName, version);

			_logger ??= SnipeServices.LogService.GetLogger("SnipeTable");
			_logger.LogTrace("Loading table " + url);

			int retry = 0;
			while (!loaded && retry <= 2)
			{
				if (cancellation.IsCancellationRequested)
				{
					_logger.LogTrace("Failed to load table - {tableName}   (task canceled)", tableName);
					return false;
				}

				if (retry > 0)
				{
					await AlterTask.Delay(100, cancellation);
					_logger.LogTrace("Retry #{retry} to load table - {tableName}", retry, tableName);
				}

				retry++;

				var stream = await InternalLoad(tableName, url, cancellation);
				if (stream == null)
				{
					continue;
				}

				try
				{
#if UNITY_WEBGL
					SnipeTableGZipReader.Read(wrapperType, items, stream);
#else
					await SnipeTableGZipReader.ReadAsync(wrapperType, items, stream);
#endif

					loaded = true;

					if (version > 0)
					{
						_logger.LogTrace("Table ready - {tableName}", tableName);
					}

					stream.Position = 0;
					await SnipeTableSaver.SaveToCacheAsync(stream, tableName, version);
				}
				catch (Exception e)
				{
					_logger.LogTrace("Failed to parse table - {tableName} - {e}", tableName, e);
				}
				finally
				{
					stream.Dispose();
				}
			}

			return loaded;
		}

		private async UniTask<MemoryStream> InternalLoad(string tableName, string url, CancellationToken cancellation)
		{
			try
			{
				using var webRequest = UnityWebRequest.Get(url);
				webRequest.timeout = WEB_REQUEST_TIMEOUT_SECONDS;
				webRequest.downloadHandler = new DownloadHandlerBuffer();
				var loadingOperation = webRequest.SendWebRequest();
				using var loadingResult = await loadingOperation;

				if (cancellation.IsCancellationRequested)
				{
					_logger.LogTrace("Failed to load table - {tableName}   (task canceled)", tableName);
					return null;
				}

				if (!loadingResult.isDone)
				{
					_logger.LogTrace("Failed to load table - {tableName}   (timeout)", tableName);
					return null;
				}

				if (!string.IsNullOrEmpty(loadingResult.error))
				{
					_logger.LogTrace("Failed to load table - {tableName}   (loader failed) {error}", tableName, loadingResult.error);
					return null;
				}

				if (loadingResult.result != UnityWebRequest.Result.Success)
				{
					_logger.LogTrace($"Failed to load table - {tableName}   (loader failed) - {(int)loadingResult.responseCode} {loadingResult.responseCode}");
					return null;
				}

				var stream = new MemoryStream(loadingResult.downloadHandler.data);
				stream.Position = 0;
				return stream;
			}
			catch (Exception e)
			{
				_logger.LogTrace("Failed to load table - {tableName} - {e}", tableName, e);
			}

			return null;
		}

		private static string GetTableUrl(string table_name, long version)
		{
			return $"{TablesConfig.GetTablesPath()}{version}_{table_name}.json.gz";
		}
	}
}
