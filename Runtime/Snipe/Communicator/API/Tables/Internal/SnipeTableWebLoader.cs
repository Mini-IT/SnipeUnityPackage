using System;
using System.Collections;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableWebLoader
	{
		private readonly HttpClient _httpClient;
		
		public SnipeTableWebLoader(HttpClient httpClient)
		{
			_httpClient = httpClient;
		}
		
		public async Task<bool> LoadAsync(Type wrapperType, IDictionary items, string table_name, long version, CancellationToken cancellation)
		{
			bool loaded = false;
			
			string url = GetTableUrl(table_name, version);
			DebugLogger.Log("[SnipeTable] Loading table " + url);

			int retry = 0;
			while (!loaded && retry <= 2)
			{
				if (cancellation.IsCancellationRequested)
				{
					DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name}   (task canceled)");
					return false;
				}

				if (retry > 0)
				{
					await Task.Delay(100, cancellation);
					DebugLogger.Log($"[SnipeTable] Retry #{retry} to load table - {table_name}");
				}

				retry++;
				HttpResponseMessage response = null;

				try
				{
					var loader_task = _httpClient.GetAsync(url, cancellation);
					Task finished_task = await Task.WhenAny(loader_task, Task.Delay(3000, cancellation));

					if (cancellation.IsCancellationRequested)
					{
						DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name}   (task canceled)");
						return false;
					}

					if (finished_task != loader_task)
					{
						DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name}   (timeout)");
						return false;
					}

					if (loader_task.IsFaulted || loader_task.IsCanceled || loader_task.Result == null || !loader_task.Result.IsSuccessStatusCode)
					{
						DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name}   (loader failed)");
						continue;
					}

					response = loader_task.Result;
				}
				catch (Exception e)
				{
					DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name} - {e}");
				}

				if (response != null)
				{
					try
					{
						using (var file_content_stream = await response.Content.ReadAsStreamAsync())
						{
							await SnipeTableGZipParser.ReadAsync(wrapperType, items, file_content_stream);
							loaded = true;
						}

						if (loaded && version > 0)
						{
							DebugLogger.Log("[SnipeTable] Table ready - " + table_name);

							// "using" block in ReadGZip closes the stream. We need to open it again
							using (var file_content_stream = await response.Content.ReadAsStreamAsync())
							{
								SnipeTableSaver.SaveToCache(file_content_stream, table_name, version);
							}
						}
					}
					catch (Exception e)
					{
						DebugLogger.Log($"[SnipeTable] Failed to parse table - {table_name} - {e}");
					}
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
