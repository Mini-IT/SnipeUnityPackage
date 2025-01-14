using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Snipe.Tables;
using MiniIT.Threading;
using MiniIT.Unity;

namespace MiniIT.Snipe
{
	public class TablesLoader
	{
		private const int MAX_LOADERS_COUNT = 4;

		private Dictionary<string, long> _versions = null;

		private CancellationTokenSource _cancellation;

		private readonly AlterSemaphore _semaphore = new AlterSemaphore(MAX_LOADERS_COUNT);

		private bool _failed = false;

		private HashSet<TablesLoaderItem> _loadingItems;
		private readonly TablesVersionsLoader _versionsLoader;
		private readonly BuiltInTablesListService _builtInTablesListService;
		private readonly SnipeAnalyticsTracker _analyticsTracker;
		private readonly ILogger _logger;

		public TablesLoader()
		{
			StreamingAssetsReader.Initialize();
			_analyticsTracker = SnipeServices.Analytics.GetTracker();
			_builtInTablesListService = new BuiltInTablesListService();
			_versionsLoader = new TablesVersionsLoader(_builtInTablesListService, _analyticsTracker);
			_logger = SnipeServices.LogService.GetLogger(nameof(TablesLoader));
		}

		internal static string GetCacheDirectoryPath()
		{
			return Path.Combine(SnipeServices.ApplicationInfo.PersistentDataPath ?? "", "SnipeTables");
		}

		internal static string GetCachePath(string tableName, long version)
		{
			return Path.Combine(GetCacheDirectoryPath(), $"{version}_{tableName}.json.gz");
		}

		public void Reset()
		{
			_logger.LogTrace("Reset");
			_analyticsTracker.TrackEvent("TablesLoader - Reset");

			StopLoading();

			_versions = null;
			_failed = false;
		}

		public void Add<TItem>(SnipeTable<TItem> table, string name)
			where TItem : SnipeTableItem, new()
		{
			_loadingItems ??= new HashSet<TablesLoaderItem>();
			_loadingItems.Add(new TablesLoaderItem(typeof(SnipeTableItemsListWrapper<TItem>), table, name));
		}

		public async UniTask<bool> Load(CancellationToken cancellationToken = default)
		{
			bool fallbackEnabled = (TablesConfig.Versioning != TablesConfig.VersionsResolution.ForceExternal);
			bool loadExternal = (TablesConfig.Versioning != TablesConfig.VersionsResolution.ForceBuiltIn);

			await _builtInTablesListService.InitializeAsync(cancellationToken);

			if (fallbackEnabled)
			{
				RemoveOutdatedCache();
			}

			bool loaded = await LoadAll(loadExternal);

			if (loaded)
			{
				RemoveMisversionedCache();
			}
			else if (loadExternal && fallbackEnabled)
			{
				_versions = null;
				loaded = await LoadAll(false);
			}

			_analyticsTracker.TrackEvent($"TablesLoader - " + (loaded ? "Loaded" : "Failed"));

			return loaded;
		}

		private async UniTask<bool> LoadAll(bool loadExternal)
		{
			StopLoading();

			if (_loadingItems == null || _loadingItems.Count == 0)
			{
				return true;
			}

			_cancellation = new CancellationTokenSource();
			CancellationToken cancellationToken = _cancellation.Token;

			IHttpClient httpClient = loadExternal ? SnipeServices.HttpClientFactory.CreateHttpClient() : null;

			_versions = await _versionsLoader.Load(httpClient, cancellationToken);

			if (cancellationToken.IsCancellationRequested || _loadingItems == null)
			{
				return false;
			}

			_failed = false;
			var tasks = new List<UniTask>(_loadingItems.Count);
			foreach (var item in _loadingItems)
			{
				tasks.Add(LoadTable(item, httpClient, cancellationToken));
			}

			await UniTask.WhenAll(tasks);

			if (httpClient is IDisposable disposableHttpClient)
			{
				disposableHttpClient.Dispose();
			}

			_cancellation?.Dispose();
			_cancellation = null;

			return !_failed;
		}

		private async UniTask LoadTable(TablesLoaderItem loaderItem, IHttpClient httpClient, CancellationToken cancellationToken)
		{
			bool loaded = false;
			bool cancelled = false;
			Exception exception = null;

			bool semaphoreOccupied = false;

			try
			{
				await _semaphore.WaitAsync(cancellationToken);
				semaphoreOccupied = true;

				if (!cancellationToken.IsCancellationRequested)
				{
					long version = 0;
					_versions?.TryGetValue(loaderItem.Name, out version);
					loaded = await LoadTableAsync(loaderItem, httpClient, version, cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
				cancelled = true;
			}
			catch (Exception e)
			{
				exception = e;
				_logger.LogTrace($"Load {loaderItem.Name} - Exception: {e}");
			}
			finally
			{
				if(semaphoreOccupied)
				{
					_semaphore.Release();
				}
			}

			if (!loaded && !_failed)
			{
				_failed = true;
				_logger.LogWarning($"Loading failed: {loaderItem.Name}. StopLoading.");

				if (!cancelled)
				{
					_analyticsTracker.TrackError($"Tables - Failed to load table '{loaderItem.Name}'", exception);
				}

				StopLoading();
			}
		}

		private async UniTask<bool> LoadTableAsync(TablesLoaderItem loaderItem, IHttpClient httpClient, long version, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				_logger.LogTrace("Failed to load table - {0}   (task canceled)", loaderItem.Name);
				return false;
			}

			_logger.LogTrace("LoadTask start - {0}", loaderItem.Name);

			// Try to load from cache
			if (await LoadTableAsync(loaderItem.Table,
				SnipeTable.LoadingLocation.Cache,
				SnipeTableFileLoader.LoadAsync(
					loaderItem.WrapperType,
					loaderItem.Table.GetItems(),
					loaderItem.Name,
					version)))
			{
				return true;
			}

			// If loading from cache failed
			// try to load a built-in file
			if (await LoadTableAsync(loaderItem.Table,
				SnipeTable.LoadingLocation.BuiltIn,
				new SnipeTableStreamingAssetsLoader(_builtInTablesListService).LoadAsync(
					loaderItem.WrapperType,
					loaderItem.Table.GetItems(),
					loaderItem.Name,
					version,
					cancellationToken)))
			{
				return true;
			}

			// If loading from cache failed
			// try loading from web
			return await LoadTableAsync(loaderItem.Table,
				SnipeTable.LoadingLocation.Network,
				new SnipeTableWebLoader().LoadAsync(
					httpClient,
					loaderItem.WrapperType,
					loaderItem.Table.GetItems(),
					loaderItem.Name,
					version,
					cancellationToken));
		}

		private async UniTask<bool> LoadTableAsync(SnipeTable table, SnipeTable.LoadingLocation loadingLocation, UniTask<bool> task)
		{
			bool loaded = await task;
			if (loaded)
			{
				table.Loaded = true;
				table.LoadingFailed = false;
				table.LoadedFrom = loadingLocation;
			}
			return loaded;
		}

		private void StopLoading()
		{
			if (_cancellation != null)
			{
				_cancellation.Cancel();
				_cancellation.Dispose();
				_cancellation = null;
			}
		}

		/// <summary>
		/// Remove cache files with versions that don't match the newly loaded ones
		/// </summary>
		private void RemoveMisversionedCache()
		{
			if (_versions == null || _versions.Count == 0)
				return;

			string directory = GetCacheDirectoryPath();
			if (!Directory.Exists(directory))
				return;

			string extention = ".json.gz";
			var files = Directory.EnumerateFiles(directory, $"*{extention}");
			foreach (string filePath in files)
			{
				if (!TryExtractNameAndVersion(filePath, out string tableName, out string version, extention) ||
					!_versions.TryGetValue(tableName, out long tableVersion) ||
					Convert.ToInt64(version) != tableVersion)
				{
					_logger.LogTrace($"RemoveMisversionedCache - Delete {filePath}");
					File.Delete(filePath);
				}
			}
		}

		/// <summary>
		/// Remove cache files with versions older than the built-in ones
		/// </summary>
		private void RemoveOutdatedCache()
		{
			string directory = GetCacheDirectoryPath();
			if (!Directory.Exists(directory))
				return;

			string extention = ".json.gz";
			var files = Directory.EnumerateFiles(directory, $"*{extention}");
			foreach (string filePath in files)
			{
				if (TryExtractNameAndVersion(filePath, out string tableName, out string version, extention) &&
					_builtInTablesListService.TryGetTableVersion(tableName.ToLower(), out long builtInVersion))
				{
					long cachedVersion = Convert.ToInt64(version);
					if (cachedVersion < builtInVersion)
					{
						_logger.LogTrace($"RemoveOutdatedCache - Delete {filePath}");
						File.Delete(filePath);
					}
				}
			}
		}

		private bool TryExtractNameAndVersion(string filePath, out string name, out string version, string extension)
		{
			string fileName = Path.GetFileName(filePath.Substring(0, filePath.Length - extension.Length));

			int underscoreIndex = fileName.IndexOf("_");
			if (underscoreIndex < 1)
			{
				name = null;
				version = null;
				return false;
			}

			version = fileName.Substring(0, underscoreIndex);
			name = fileName.Substring(underscoreIndex + 1);
			return true;
		}
	}
}
