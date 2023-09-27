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
		public async Task<bool> LoadAsync(Type wrapperType, IDictionary items, string tableName, long version, CancellationToken cancellation)
		{
			bool loaded = false;
			
			string url = GetTableUrl(tableName, version);

			var logger = SnipeServices.LogService.GetLogger("SnipeTable");
			logger.LogTrace("[SnipeTable] Loading table " + url);

			int retry = 0;
			while (!loaded && retry <= 2)
			{
				if (cancellation.IsCancellationRequested)
				{
					logger.LogTrace($"[SnipeTable] Failed to load table - {tableName}   (task canceled)");
					return false;
				}

				if (retry > 0)
				{
					await Task.Delay(100, cancellation);
					logger.LogTrace($"[SnipeTable] Retry #{retry} to load table - {tableName}");
				}

				retry++;
				HttpWebResponse response = null;

				try
				{
					var webRequest = WebRequest.Create(new Uri(url));
					var loadTask = webRequest.GetResponseAsync();
					Task finished_task = await Task.WhenAny(loadTask, Task.Delay(3000, cancellation));

					if (cancellation.IsCancellationRequested)
					{
						logger.LogTrace($"[SnipeTable] Failed to load table - {tableName}   (task canceled)");
						return false;
					}

					if (finished_task != loadTask)
					{
						logger.LogTrace($"[SnipeTable] Failed to load table - {tableName}   (timeout)");
						return false;
					}

					if (loadTask.IsFaulted || loadTask.IsCanceled)
					{
						logger.LogTrace($"[SnipeTable] Failed to load table - {tableName}   (loader failed)");
						continue;
					}

					response = (HttpWebResponse)loadTask.Result;

					if (response == null || !new HttpResponseMessage(response.StatusCode).IsSuccessStatusCode)
					{
						logger.LogTrace($"[SnipeTable] Failed to load table - {tableName}   (loader failed)");
						response.Dispose();
						continue;
					}
				}
				catch (Exception e)
				{
					logger.LogTrace($"[SnipeTable] Failed to load table - {tableName} - {e}");
				}

				if (response != null)
				{
					try
					{
						using (var stream = new MemoryStream())
						{
							using (var contentStream = response.GetResponseStream())
							{
								contentStream.CopyTo(stream);
							}

							stream.Position = 0;
							await SnipeTableGZipReader.ReadAsync(wrapperType, items, stream);

							loaded = true;

							if (version > 0)
							{
								logger.LogTrace("[SnipeTable] Table ready - " + tableName);
							}

							stream.Position = 0;
							SnipeTableSaver.SaveToCache(stream, tableName, version);
						}
					}
					catch (Exception e)
					{
						logger.LogTrace($"[SnipeTable] Failed to parse table - {tableName} - {e}");
					}

					response.Dispose();
				}
			}

			return loaded;
		}
		
		private static string GetTableUrl(string table_name, long version)
		{
			return $"{TablesConfig.GetTablesPath()}{version}_{table_name}.json.gz";
		}
	}
}
