using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Threading;

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

		public async UniTask<Dictionary<string, long>> Load(IHttpClient httpClient, CancellationToken cancellationToken)
		{
			Dictionary<string, long> versions = null;

			bool loadExternal = httpClient != null;

			if (loadExternal)
			{
				versions = await LoadFromWeb(httpClient, cancellationToken);
			}

			if (versions == null)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					_logger.LogTrace("LoadVersion task canceled");
				}
				else
				{
					if (loadExternal)
					{
						_logger.LogTrace("LoadVersion Failed. Trying to use the built-in ones");
						_analyticsTracker.TrackEvent("Tables - LoadVersion Failed");
					}

					versions = await LoadBuiltIn();
				}
			}

			return versions;
		}

		private async UniTask<Dictionary<string, long>> LoadFromWeb(IHttpClient httpClient, CancellationToken cancellationToken)
		{
			Dictionary<string, long> versions = null;

			const int MAX_RETIES = 3;
			for (int retriesCount = 0; retriesCount < MAX_RETIES; retriesCount++)
			{
				if (retriesCount > 0)
				{
					try
					{
						await AlterTask.Delay(500, cancellationToken);
					}
					catch (OperationCanceledException)
					{
						_logger.LogTrace("LoadVersion task canceled");
						break;
					}
				}

				string url = GetVersionsUrl();

				_logger.LogTrace("LoadVersion ({count}) {url}", retriesCount, url);

				try
				{
					var webRequest = await httpClient.Get(new Uri(url), TimeSpan.FromSeconds(1));

					if (webRequest.IsSuccess)
					{
						string json = await webRequest.GetStringContentAsync();
						versions = ParseVersionsJson(json);

						if (versions == null)
						{
							_analyticsTracker.TrackEvent("Tables - LoadVersion Failed to prase versions json", new Dictionary<string, object>()
							{
								["url"] = url,
								["json"] = json,
							});
						}
						else
						{
							_logger.LogTrace("LoadVersion done - {count} items", versions.Count);
						}

						break;
					}
					else
					{
						long responseCode = webRequest.ResponseCode;

						_analyticsTracker.TrackEvent("Tables - LoadVersion Failed to load url", new Dictionary<string, object>()
						{
							["HttpStatus"] = (HttpStatusCode)responseCode,
							["HttpStatusCode"] = responseCode,
							["url"] = url,
						});

						if (responseCode == (long)HttpStatusCode.NotFound)
						{
							// HTTP Status: 404
							// It is useless to retry loading
							_logger.LogTrace("LoadVersion StatusCode = {code} - will not rety", responseCode);
							break;
						}
					}
				}
				catch (Exception e) when (e is AggregateException ae && ae.InnerException is HttpRequestException)
				{
					_logger.LogTrace("LoadVersion HttpRequestException - network is unreachable - will not rety. {e}", e);
					break;
				}
				catch (Exception e) when (e is OperationCanceledException ||
						e is AggregateException ae && ae.InnerException is OperationCanceledException)
				{
					_logger.LogTrace("LoadVersion - TaskCanceled");
					break;
				}
				catch (Exception e)
				{
					_logger.LogTrace("LoadVersion - Exception: {e}", e);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					_logger.LogTrace("LoadVersion task canceled");
					break;
				}
			}

			return versions;
		}

		private async UniTask<Dictionary<string, long>> LoadBuiltIn()
		{
			while (_builtInTablesListService.Items == null)
			{
				await AlterTask.Delay(50);
			}

			var versions = new Dictionary<string, long>(_builtInTablesListService.Items.Count);
			foreach (var item in _builtInTablesListService.Items)
			{
				versions[item.name] = item.version;
			}

			return versions;
		}

		private static string GetVersionsUrl()
		{
			return TablesConfig.GetTablesPath(true) + "version.json";
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

			_logger.LogTrace("Faield to prase versions json");
			return null;
		}
	}
}
