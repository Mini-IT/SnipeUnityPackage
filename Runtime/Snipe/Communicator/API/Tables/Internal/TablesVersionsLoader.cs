using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public class TablesVersionsLoader
	{
		public async Task<Dictionary<string, long>> Load(CancellationToken cancellationToken)
		{
			Dictionary<string, long> versions = null;

			for (int retries_count = 0; retries_count < 3; retries_count++)
			{
				string url = GetVersionsUrl();

				DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion ({retries_count}) " + url);
				
				try
				{
					var webRequest = WebRequest.Create(new Uri(url));
					var loadTask = webRequest.GetResponseAsync();
					if (await Task.WhenAny(loadTask, Task.Delay(1000, cancellationToken)) == loadTask)
					{
						using (HttpWebResponse response = (HttpWebResponse)loadTask.Result)
						{
							string json = null;
							using (var reader = new StreamReader(response.GetResponseStream()))
							{
								json = reader.ReadToEnd();
							}

							if (!string.IsNullOrEmpty(json))
							{
								versions = ParseVersionsJson(json);

								if (versions == null)
								{
									Analytics.GetInstance().TrackEvent("Tables - LoadVersion Failed to prase versions json", new SnipeObject()
									{
										["url"] = url,
										["json"] = json,
									});
								}
								else
								{
									DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion done - {versions.Count} items");
								}

								break;
							}
							else
							{
								Analytics.GetInstance().TrackEvent("Tables - LoadVersion Failed to load url", new SnipeObject()
								{
									["HttpStatus"] = response.StatusCode,
									["HttpStatusCode"] = (int)response.StatusCode,
									["url"] = url,
								});

								if (response.StatusCode == HttpStatusCode.NotFound)
								{
									// HTTP Status: 404
									// It is useless to retry loading
									DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion StatusCode = {response.StatusCode} - will not rety");
									break;
								}
							}
						}
					}
				}
				catch (Exception e) when (e is AggregateException ae && ae.InnerException is HttpRequestException)
				{
					DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion HttpRequestException - network is unreachable - will not rety. {e}");
					break;
				}
				catch (Exception e) when (e is OperationCanceledException ||
						e is AggregateException ae && ae.InnerException is OperationCanceledException)
				{
					DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion - TaskCanceled");
					break;
				}
				catch (Exception e)
				{
					DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion - Exception: {e}");
				}
				
				if (cancellationToken.IsCancellationRequested)
				{
					DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion task canceled");
					break;
				}

				try
				{
					await Task.Delay(500, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion task canceled");
					break;
				}
			}

			if (versions == null)
			{
				DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] LoadVersion Failed");
				Analytics.GetInstance().TrackEvent("Tables - LoadVersion Failed");
				return null;
			}

			return versions;
		}

		private string GetVersionsUrl()
		{
			return $"{TablesConfig.GetTablesPath(true)}version.json";
		}

		private Dictionary<string, long> ParseVersionsJson(string json)
		{
			var content = (Dictionary<string, object>)fastJSON.JSON.Parse(json);
			if (content != null && content["tables"] is IList tables)
			{
				var versions = new Dictionary<string, long>(tables.Count);
				foreach (var item in tables)
				{
					if (item is Dictionary<string, object> table &&
						table.TryGetValue("name", out var name) &&
						table.TryGetValue("version", out var version))
					{
						versions[(string)name] = (long)version;
					}
				}
				return versions;
			}

			DebugLogger.Log($"[{nameof(TablesVersionsLoader)}] Faield to prase versions json");
			return null;
		}
	}
}
