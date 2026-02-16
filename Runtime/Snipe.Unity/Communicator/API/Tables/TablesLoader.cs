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
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public class TablesLoader
	{
		private const int MAX_LOADERS_COUNT = 1;

		private Dictionary<string, long> _versions = null;

		private CancellationTokenSource _cancellation;

		private readonly AlterSemaphore _subloadersSemaphore = new AlterSemaphore(MAX_LOADERS_COUNT, MAX_LOADERS_COUNT);

		private bool _failed = false;

		private HashSet<TablesLoaderItem> _loadingItems;
		private readonly TablesVersionsLoader _versionsLoader;
		private readonly BuiltInTablesListService _builtInTablesListService;
		private readonly TablesOptions _tablesOptions;
		private readonly IAnalyticsContext _analytics;
		private readonly IInternetReachabilityProvider _internetReachabilityProvider;
		private readonly ILogger _logger;
		private readonly ISnipeServices _services;

		private UniTaskCompletionSource<bool> _loadingTaskCompletion;
		private readonly object _loadingTaskCompletionLock = new();

		public TablesLoader(ISnipeServices services, TablesOptions tablesOptions)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_tablesOptions = tablesOptions ?? throw new ArgumentNullException(nameof(tablesOptions));
			StreamingAssetsReader.Initialize();
			_analytics = (_services.Analytics as IAnalyticsTrackerProvider)?.GetTracker();
			_internetReachabilityProvider = _services.InternetReachabilityProvider;
			_builtInTablesListService = new BuiltInTablesListService(_services.LogService.GetLogger(nameof(BuiltInTablesListService)));
			_versionsLoader = new TablesVersionsLoader(_builtInTablesListService, _tablesOptions, _analytics, _services.LogService.GetLogger(nameof(TablesVersionsLoader)));
			_logger = _services.LogService.GetLogger(nameof(TablesLoader));
		}

		internal static string GetCacheDirectoryPath(ISnipeServices services)
		{
			return Path.Combine(services?.ApplicationInfo.PersistentDataPath ?? "", "SnipeTables");
		}

		internal static string GetCachePath(ISnipeServices services, string tableName, long version)
		{
			return Path.Combine(GetCacheDirectoryPath(services), $"{version}_{tableName}.json.gz");
		}

		public void Reset()
		{
			_logger.LogTrace("Reset");
			_analytics.TrackEvent("TablesLoader - Reset");

			StopLoading();

			_versions = null;
			_failed = false;
			_loadingTaskCompletion = null;
		}

		public void Add<TItem>(SnipeTable<TItem> table, string name)
			where TItem : SnipeTableItem, new()
		{
			_loadingItems ??= new HashSet<TablesLoaderItem>();
			_loadingItems.Add(new TablesLoaderItem(typeof(SnipeTableItemsListWrapper<TItem>), table, name));
		}

		public UniTask<bool> Load(CancellationToken cancellationToken = default)
		{
			lock (_loadingTaskCompletionLock)
			{
				if (_loadingTaskCompletion != null)
				{
					return _loadingTaskCompletion.Task;
				}

				_loadingTaskCompletion = new UniTaskCompletionSource<bool>();
			}

			return DoLoad(cancellationToken);
		}

		private async UniTask<bool> DoLoad(CancellationToken cancellationToken = default)
		{
			StopLoading();

			try
			{
				await _builtInTablesListService.InitializeAsync(cancellationToken);

				bool fallbackEnabled = _tablesOptions.Versioning != TablesOptions.VersionsResolution.ForceExternal;
				bool loadExternal = _tablesOptions.Versioning != TablesOptions.VersionsResolution.ForceBuiltIn
				                    && _internetReachabilityProvider.IsInternetAvailable;

				if (fallbackEnabled)
				{
					RemoveOutdatedCache();
				}

				IHttpClient httpClient = loadExternal ? _services.HttpClientFactory.CreateHttpClient() : null;

				bool loaded = await LoadAll(httpClient, cancellationToken);

				if (httpClient is IDisposable disposableHttpClient)
				{
					AlterTask.RunAndForget(disposableHttpClient.Dispose, CancellationToken.None);
				}

				if (loaded)
				{
					RemoveMisversionedCache();
				}
				else if (loadExternal && fallbackEnabled)
				{
					_versions = null;
					loaded = await LoadAll(null, cancellationToken);
				}

				_analytics.TrackEvent($"TablesLoader - " + (loaded ? "Loaded" : "Failed"));
				_loadingTaskCompletion.TrySetResult(loaded);
				return loaded;
			}
			catch (OperationCanceledException)
			{
				_loadingTaskCompletion.TrySetCanceled();
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogError($"Tables loading failed: {ex}");
				_loadingTaskCompletion.TrySetException(ex);
				throw;
			}
			finally
			{
				_loadingTaskCompletion = null;
			}
		}

		private async UniTask<bool> LoadAll(IHttpClient httpClient, CancellationToken cancellationToken = default)
		{
			if (_loadingItems == null || _loadingItems.Count == 0)
			{
				return true;
			}

			_cancellation = new CancellationTokenSource();
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token);
			CancellationToken linkedToken = linkedCts.Token;

			try
			{
				var versionsLoadResult = await _versionsLoader.Load(httpClient, linkedToken);
				_versions = versionsLoadResult.Vesions;

				if (linkedToken.IsCancellationRequested || _loadingItems == null)
				{
					return false;
				}

				if (!versionsLoadResult.LoadedFromWeb)
				{
					httpClient = null;
				}

				_failed = false;
				var tasks = new List<UniTask>(_loadingItems.Count);
				foreach (var item in _loadingItems)
				{
					tasks.Add(LoadTable(item, httpClient, linkedToken));
				}

				await UniTask.WhenAll(tasks);

				return !_failed;
			}
			finally
			{
				_cancellation?.Dispose();
				_cancellation = null;
			}
		}

		private async UniTask LoadTable(TablesLoaderItem loaderItem, IHttpClient httpClient, CancellationToken cancellationToken)
		{
			bool loaded = false;
			bool cancelled = false;
			Exception exception = null;

			bool semaphoreOccupied = false;

			try
			{
				await _subloadersSemaphore.WaitAsync(cancellationToken);
				semaphoreOccupied = true;

				if (!cancellationToken.IsCancellationRequested)
				{
					if (_versions != null && _versions.TryGetValue(loaderItem.Name, out long version))
					{
						loaded = await LoadTableAsync(loaderItem, httpClient, version, cancellationToken);
					}
					else
					{
						_logger.LogError($"Failed to get table version for table '{loaderItem.Name}'");
					}
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
				if (semaphoreOccupied)
				{
					_subloadersSemaphore.Release();
				}
			}

			if (!loaded && !_failed)
			{
				_failed = true;
				_logger.LogWarning($"Loading failed: {loaderItem.Name}. StopLoading.");

				if (!cancelled)
				{
					_analytics.TrackError($"Tables - Failed to load table '{loaderItem.Name}'", exception);
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
					_services,
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
				new SnipeTableStreamingAssetsLoader(_builtInTablesListService, _services.LogService.GetLogger("SnipeTable")).LoadAsync(
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
			if (httpClient != null)
			{
				return await LoadTableAsync(loaderItem.Table,
					SnipeTable.LoadingLocation.Network,
					new SnipeTableWebLoader(_services.LogService.GetLogger("SnipeTable"), _services, _tablesOptions).LoadAsync(
						httpClient,
						loaderItem.WrapperType,
						loaderItem.Table.GetItems(),
						loaderItem.Name,
						version,
						cancellationToken));
			}

			return false;
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
			CancellationTokenHelper.CancelAndDispose(ref _cancellation);
		}

		/// <summary>
		/// Remove cache files with versions that don't match the newly loaded ones
		/// </summary>
		private void RemoveMisversionedCache()
		{
			if (_versions == null || _versions.Count == 0)
				return;

			string directory = GetCacheDirectoryPath(_services);
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
			string directory = GetCacheDirectoryPath(_services);
			if (!Directory.Exists(directory))
				return;

			string extention = ".json.gz";
			var files = Directory.EnumerateFiles(directory, $"*{extention}");
			foreach (string filePath in files)
			{
				if (TryExtractNameAndVersion(filePath, out string tableName, out string version, extention) &&
					_builtInTablesListService.TryGetTableVersion(tableName.ToLowerInvariant(), out long builtInVersion))
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

			int underscoreIndex = fileName.IndexOf('_');
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
