using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Threading;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableWebLoader
	{
		private const int WEB_REQUEST_TIMEOUT_SECONDS = 3;

		private readonly ILogger _logger;
		private readonly ISnipeServices _services;

		public SnipeTableWebLoader(ILogger logger, ISnipeServices services)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_services = services ?? throw new ArgumentNullException(nameof(services));
		}

		public async UniTask<bool> LoadAsync(IHttpClient httpClient, Type wrapperType, IDictionary items, string tableName, long version, CancellationToken cancellation)
		{
			bool loaded = false;

			string url = GetTableUrl(tableName, version);

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

				var stream = await InternalLoad(httpClient, tableName, url, cancellation);
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
					await SnipeTableSaver.SaveToCacheAsync(_services, stream, tableName, version);
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

		private async UniTask<MemoryStream> InternalLoad(IHttpClient httpClient, string tableName, string url, CancellationToken cancellation)
		{
			try
			{
				using var loadingResult = await httpClient.Get(new Uri(url), TimeSpan.FromSeconds(WEB_REQUEST_TIMEOUT_SECONDS));

				if (cancellation.IsCancellationRequested)
				{
					_logger.LogTrace("Failed to load table - {tableName}   (task canceled)", tableName);
					return null;
				}

				// if (!loadingResult.IsSuccess)
				// {
				// 	_logger.LogTrace("Failed to load table - {tableName}   (timeout)", tableName);
				// 	return null;
				// }

				if (!loadingResult.IsSuccess)
				{
					long responseCode = loadingResult.ResponseCode;
					_logger.LogTrace("Failed to load table - {tableName}   (loader failed) {code} {codename} - {error}", tableName, responseCode, (HttpStatusCode)responseCode, loadingResult.Error);
					return null;
				}

				byte[] responseData = await loadingResult.GetBinaryContentAsync();

				var stream = new MemoryStream(responseData);
				stream.Position = 0;
				return stream;
			}
			catch (Exception e)
			{
				_logger.LogTrace("Failed to load table - {tableName} - {e}", tableName, e);
			}

			return null;
		}

		private static string GetTableUrl(string tableName, long version)
		{
			return $"{TablesConfig.GetTablesPath()}{version}_{tableName}.json.gz";
		}
	}
}
