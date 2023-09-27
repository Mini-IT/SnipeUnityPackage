using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Snipe.Logging;
using MiniIT.Snipe.Tables;
using MiniIT.Unity;

namespace MiniIT.Snipe
{
	public class TablesLoader
	{
		private const int MAX_LOADERS_COUNT = 4;

		private Dictionary<string, long> _versions = null;

		private CancellationTokenSource _cancellation;
		private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(MAX_LOADERS_COUNT);
		private bool _failed = false;

		private HashSet<TablesLoaderItem> _loadingItems; 
		private readonly TablesVersionsLoader _versionsLoader;
		private readonly SnipeAnalyticsTracker _analyticsTracker;
		private readonly ILogger _logger;
		private readonly BuiltInTablesListService _builtInTablesListService;

		public TablesLoader()
		{
			StreamingAssetsReader.Initialize();
			_versionsLoader = new TablesVersionsLoader();
			_analyticsTracker = SnipeServices.Analytics.GetTracker();
			_logger = SnipeServices.LogService.GetLogger(nameof(TablesLoader));
			_builtInTablesListService = new BuiltInTablesListService();
		}

		internal static string GetCacheDirectoryPath()
		{
			return Path.Combine(SnipeServices.ApplicationInfo.PersistentDataPath ?? "", "SnipeTables");
		}

		internal static string GetCachePath(string table_name, long version)
		{
			return Path.Combine(GetCacheDirectoryPath(), $"{version}_{table_name}.json.gz");
		}

		public void Reset()
		{
			_logger.LogTrace("Reset");

			SnipeServices.Analytics.GetTracker().TrackEvent("TablesLoader - Reset");

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

		public async Task<bool> Load(CancellationToken cancellationToken = default)
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

		private async Task<bool> LoadAll(bool loadVersion)
		{
			StopLoading();

			if (_loadingItems == null || _loadingItems.Count == 0)
				return true;

			_cancellation = new CancellationTokenSource();
			CancellationToken cancellationToken = _cancellation.Token;

			if (loadVersion)
			{
				_versions = await _versionsLoader.Load(cancellationToken);

				if (cancellationToken.IsCancellationRequested || _loadingItems == null)
					return false;
			}

			_failed = false;
			var tasks = new List<Task>(_loadingItems.Count);
			foreach (var item in _loadingItems)
			{
				tasks.Add(LoadTable(item, cancellationToken));
			}

			await Task.WhenAll(tasks);

			_cancellation = null;

			return !_failed;
		}

		private async Task LoadTable(TablesLoaderItem loaderItem, CancellationToken cancellationToken)
		{
			bool loaded = false;
			bool cancelled = false;
			Exception exception = null;

			try
			{
				await _semaphore.WaitAsync(cancellationToken);

				if (!cancellationToken.IsCancellationRequested)
				{
					long version = 0;
					_versions?.TryGetValue(loaderItem.Name, out version);
					loaded = await LoadTableAsync(loaderItem, version, cancellationToken);
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
				_semaphore.Release();
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

		private async Task<bool> LoadTableAsync(TablesLoaderItem loaderItem, long version, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				_logger.LogTrace($"Failed to load table - {loaderItem.Name}   (task canceled)");
				return false;
			}

			_logger.LogTrace($"LoadTask start - {loaderItem.Name}");

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
					loaderItem.WrapperType,
					loaderItem.Table.GetItems(),
					loaderItem.Name,
					version,
					cancellationToken));
		}

		private async Task<bool> LoadTableAsync(SnipeTable table, SnipeTable.LoadingLocation loadingLocation, Task<bool> task)
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
