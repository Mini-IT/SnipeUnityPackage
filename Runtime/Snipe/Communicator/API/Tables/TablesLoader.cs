using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MiniIT.Snipe.Tables;

namespace MiniIT.Snipe
{
	public class TablesLoader : IDisposable
	{
		private const int MAX_LOADERS_COUNT = 4;

		private Dictionary<string, long> _versions = null;

		private CancellationTokenSource _cancellation;
		private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(MAX_LOADERS_COUNT);
		private bool _failed = false;

		private List<Func<CancellationToken, Task>> _loadingTasks;
		private readonly TablesVersionsLoader _versionsLoader;

		private HttpClient _httpClient;

		public TablesLoader()
		{
			BetterStreamingAssets.Initialize();
			_httpClient = new HttpClient();
			_versionsLoader = new TablesVersionsLoader(_httpClient);
		}

		~TablesLoader()
		{
			Dispose();
		}

		public void Dispose()
		{
			_httpClient?.Dispose();
			_httpClient = null;
		}

		internal static string GetCacheDirectoryPath()
		{
			return Path.Combine(TablesConfig.PersistentDataPath, "SnipeTables");
		}

		internal static string GetCachePath(string table_name, long version)
		{
			return Path.Combine(GetCacheDirectoryPath(), $"{version}_{table_name}.json.gz");
		}

		public void Reset()
		{
			DebugLogger.Log($"[{nameof(TablesLoader)}] Reset");

			StopLoading();
			
			_versions = null;
			_failed = false;
			_loadingTasks?.Clear();
		}

		public void Add<TItem, TWrapper>(SnipeTable<TItem> table, string name)
			where TWrapper : class, ISnipeTableItemsListWrapper<TItem>, new()
			where TItem : SnipeTableItem, new()
		{
			_loadingTasks ??= new List<Func<CancellationToken, Task>>();
			_loadingTasks.Add((CancellationToken cancellationToken) =>
			{
				return LoadTable<TItem, TWrapper>(table, name, cancellationToken);
			});
		}

		public async Task<bool> Load()
		{
			bool fallbackEnabled = (TablesConfig.Versioning != TablesConfig.VersionsResolution.ForceExternal);
			bool loadExternal = (TablesConfig.Versioning != TablesConfig.VersionsResolution.ForceBuiltIn);

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

			return loaded;
		}

		private async Task<bool> LoadAll(bool loadVersion)
		{
			StopLoading();

			if (_loadingTasks == null || _loadingTasks.Count == 0)
				return true;

			_cancellation = new CancellationTokenSource();
			CancellationToken cancellationToken = _cancellation.Token;

			if (loadVersion)
			{
				_versions = await _versionsLoader.Load(cancellationToken);

				if (cancellationToken.IsCancellationRequested || _loadingTasks == null)
					return false;
			}

			_failed = false;
			var tasks = new List<Task>(_loadingTasks.Count);
			foreach (var task in _loadingTasks)
			{
				tasks.Add(Task.Run(() => task.Invoke(cancellationToken)));
			}

			await Task.WhenAll(tasks);

			_cancellation = null;

			return !_failed;
		}

		private async Task LoadTable<TItem, TWrapper>(SnipeTable<TItem> table, string name, CancellationToken cancellationToken)
			where TItem : SnipeTableItem, new()
			where TWrapper : class, ISnipeTableItemsListWrapper<TItem>, new()
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
					_versions?.TryGetValue(name, out version);
					loaded = await LoadTableAsync<TItem, TWrapper>(table, name, version, cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
				cancelled = true;
			}
			catch (Exception e)
			{
				exception = e;
				DebugLogger.Log($"[{nameof(TablesLoader)}] Load {name} - Exception: {e}");
			}
			finally
			{
				_semaphore.Release();
			}

			if (!loaded && !_failed)
			{
				_failed = true;
				DebugLogger.LogWarning($"[{nameof(TablesLoader)}] Loading failed: {name}. StopLoading.");

				if (!cancelled)
				{
					Analytics.GetInstance().TrackError($"Tables - Failed to load table '{name}'", exception);
				}

				StopLoading();
			}
		}

		private async Task<bool> LoadTableAsync<TItem, TWrapper>(SnipeTable<TItem> table, string table_name, long version, CancellationToken cancellation)
			where TItem : SnipeTableItem, new()
			where TWrapper : class, ISnipeTableItemsListWrapper<TItem>, new()
		{
			if (cancellation.IsCancellationRequested)
			{
				DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name}   (task canceled)");
				return false;
			}

			DebugLogger.Log($"[SnipeTable] LoadTask start - {table_name}");

			// Try to load from cache
			if (await LoadTableAsync(table,
				SnipeTable.LoadingLocation.Cache,
				SnipeTableFileLoader.LoadAsync<TItem, TWrapper>(table._items, table_name, version)))
			{
				return true;
			}

			// If loading from cache failed
			// try to load a built-in file
			if (await LoadTableAsync(table,
				SnipeTable.LoadingLocation.BuiltIn,
				SnipeTableStreamingAssetsLoader.LoadAsync<TItem, TWrapper>(table._items, table_name, version)))
			{
				return true;
			}

			// If loading from cache failed
			// try loading from web
			return await LoadTableAsync(table,
				SnipeTable.LoadingLocation.Network,
				new SnipeTableWebLoader(_httpClient).LoadAsync<TItem, TWrapper>(table._items, table_name, version, cancellation));
		}

		private async Task<bool> LoadTableAsync<TItem>(SnipeTable<TItem> table, SnipeTable.LoadingLocation loadingLocation, Task<bool> task)
			where TItem : SnipeTableItem, new()
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
				_cancellation?.Dispose();
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
					DebugLogger.Log($"[{nameof(TablesLoader)}] RemoveMisversionedCache - Delete {filePath}");
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

			var versions = new Dictionary<string, long>();

			string extention = ".jsongz";
			var builtinfiles = BetterStreamingAssets.GetFiles("/", $"*{extention}");
			foreach (var file in builtinfiles)
			{
				if (TryExtractNameAndVersion(file, out string tableName, out string version, extention))
				{
					versions[tableName.ToLower()] = Convert.ToInt64(version);
				}
			}

			extention = ".json.gz";
			var files = Directory.EnumerateFiles(directory, $"*{extention}");
			foreach (string filePath in files)
			{
				if (TryExtractNameAndVersion(filePath, out string tableName, out string version, extention) &&
					versions.TryGetValue(tableName.ToLower(), out long builtInVersion))
				{
					long cachedVersion = Convert.ToInt64(version);
					if (cachedVersion < builtInVersion)
					{
						DebugLogger.Log($"[{nameof(TablesLoader)}] RemoveOutdatedCache - Delete {filePath}");
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
