using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableWebLoader
	{
		private ILogger _logger;

		public async Task<bool> LoadAsync(Type wrapperType, IDictionary items, string tableName, long version, CancellationToken cancellation)
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
					_logger.LogTrace($"Failed to load table - {tableName}   (task canceled)");
					return false;
				}

				if (retry > 0)
				{
					await Task.Delay(100, cancellation);
					_logger.LogTrace($"Retry #{retry} to load table - {tableName}");
				}

				retry++;

				var stream = await InternalLoad(tableName, url, cancellation);
				if (stream == null)
				{
					continue;
				}

				try
				{
					await SnipeTableGZipReader.ReadAsync(wrapperType, items, stream);

					loaded = true;

					if (version > 0)
					{
						_logger.LogTrace("Table ready - " + tableName);
					}

					stream.Position = 0;
					SnipeTableSaver.SaveToCache(stream, tableName, version);
				}
				catch (Exception e)
				{
					_logger.LogTrace($"Failed to parse table - {tableName} - {e}");
				}
				finally
				{
					stream.Dispose();
				}
			}

			return loaded;
		}

		private async Task<MemoryStream> InternalLoad(string tableName, string url, CancellationToken cancellation)
		{
			HttpWebResponse response = null;

			try
			{
				var webRequest = WebRequest.Create(new Uri(url));
				var loadTask = webRequest.GetResponseAsync();
				Task finishedTask = await Task.WhenAny(loadTask, Task.Delay(3000, cancellation));

				if (cancellation.IsCancellationRequested)
				{
					_logger.LogTrace($"Failed to load table - {tableName}   (task canceled)");
					return null;
				}

				if (finishedTask != loadTask)
				{
					_logger.LogTrace($"Failed to load table - {tableName}   (timeout)");
					return null;
				}

				if (loadTask.IsFaulted || loadTask.IsCanceled)
				{
					_logger.LogTrace($"Failed to load table - {tableName}   (loader failed)");
					return null;
				}

				response = (HttpWebResponse)loadTask.Result;

				if (response == null)
				{
					_logger.LogTrace($"Failed to load table - {tableName}   (loader failed)");
					return null;
				}

				if (!new HttpResponseMessage(response.StatusCode).IsSuccessStatusCode)
				{
					_logger.LogTrace($"Failed to load table - {tableName}   (loader failed) - {(int)response.StatusCode} {response.StatusCode}");
					return null;
				}

				var stream = new MemoryStream();
				response.GetResponseStream().CopyTo(stream);
				stream.Position = 0;
				return stream;
			}
			catch (Exception e)
			{
				_logger.LogTrace($"Failed to load table - {tableName} - {e}");
			}
			finally
			{
				response?.Dispose();
			}

			return null;
		}

		private static string GetTableUrl(string table_name, long version)
		{
			return $"{TablesConfig.GetTablesPath()}{version}_{table_name}.json.gz";
		}
	}
}
