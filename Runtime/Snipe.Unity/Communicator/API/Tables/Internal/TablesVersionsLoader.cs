using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Tables
{
	public class TablesVersionsLoader
	{
		private readonly BuiltInTablesListService _builtInTablesListService;
		private readonly SnipeAnalyticsTracker _analyticsTracker;
		private readonly ILogger _logger;

		public TablesVersionsLoader(BuiltInTablesListService builtInTablesListService, SnipeAnalyticsTracker analyticsTracker)
		{
			_builtInTablesListService = builtInTablesListService;
			_analyticsTracker = analyticsTracker;
			_logger = SnipeServices.LogService.GetLogger(nameof(TablesVersionsLoader));
		}

		public async Task<Dictionary<string, long>> Load(CancellationToken cancellationToken, bool loadExternal)
		{
			Dictionary<string, long> versions = null;

			if (loadExternal)
			{
				versions = await LoadFromWeb(cancellationToken);
			}

			if (versions == null)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					_logger.LogTrace($"LoadVersion task canceled");
				}
				else
				{
					_logger.LogTrace($"LoadVersion Failed. Trying to use the built-in ones");
					_analyticsTracker.TrackEvent("Tables - LoadVersion Failed");

					versions = await LoadBuiltIn();
				}
			}

			return versions;
		}

		private async Task<Dictionary<string, long>> LoadFromWeb(CancellationToken cancellationToken)
		{
			Dictionary<string, long> versions = null;

			const int MAX_RETIES = 3;
			for (int retries_count = 0; retries_count < MAX_RETIES; retries_count++)
			{
				if (retries_count > 0)
				{
					try
					{
						await Task.Delay(500, cancellationToken);
					}
					catch (OperationCanceledException)
					{
						_logger.LogTrace($"LoadVersion task canceled");
						break;
					}
				}

				string url = GetVersionsUrl();

				_logger.LogTrace($"LoadVersion ({retries_count}) " + url);
				
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
									_analyticsTracker.TrackEvent("Tables - LoadVersion Failed to prase versions json", new SnipeObject()
									{
										["url"] = url,
										["json"] = json,
									});
								}
								else
								{
									_logger.LogTrace($"LoadVersion done - {versions.Count} items");
								}

								break;
							}
							else
							{
								_analyticsTracker.TrackEvent("Tables - LoadVersion Failed to load url", new SnipeObject()
								{
									["HttpStatus"] = response.StatusCode,
									["HttpStatusCode"] = (int)response.StatusCode,
									["url"] = url,
								});

								if (response.StatusCode == HttpStatusCode.NotFound)
								{
									// HTTP Status: 404
									// It is useless to retry loading
									_logger.LogTrace($"LoadVersion StatusCode = {response.StatusCode} - will not rety");
									break;
								}
							}
						}
					}
				}
				catch (Exception e) when (e is AggregateException ae && ae.InnerException is HttpRequestException)
				{
					_logger.LogTrace($"LoadVersion HttpRequestException - network is unreachable - will not rety. {e}");
					break;
				}
				catch (Exception e) when (e is OperationCanceledException ||
						e is AggregateException ae && ae.InnerException is OperationCanceledException)
				{
					_logger.LogTrace($"LoadVersion - TaskCanceled");
					break;
				}
				catch (Exception e)
				{
					_logger.LogTrace($"LoadVersion - Exception: {e}");
				}
				
				if (cancellationToken.IsCancellationRequested)
				{
					_logger.LogTrace($"LoadVersion task canceled");
					break;
				}
			}

			return versions;
		}

		private async Task<Dictionary<string, long>> LoadBuiltIn()
		{
			while (_builtInTablesListService.Items == null)
			{
				await Task.Delay(50);
			}

			var versions = new Dictionary<string, long>(_builtInTablesListService.Items.Count);
			foreach (var item in _builtInTablesListService.Items)
			{
				versions[item.name] = item.version;
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

			_logger.LogTrace($"Faield to prase versions json");
			return null;
		}
	}
}
