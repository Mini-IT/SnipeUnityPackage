using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class TablesLoader
	{
		private const int MAX_LOADERS_COUNT = 4;

		private Dictionary<string, long> _versions = null;

		private CancellationTokenSource _cancellation;
		private SemaphoreSlim _semaphore;
		private bool _failed = false;

		private List<Func<CancellationToken, Task>> _loadingTasks;

		public TablesLoader()
		{
			BetterStreamingAssets.Initialize();
		}

		internal static string GetCacheDirectoryPath()
		{
			return Path.Combine(SnipeConfig.PersistentDataPath, "SnipeTables");
		}

		internal static string GetCachePath(string table_name, long version)
		{
			return Path.Combine(GetCacheDirectoryPath(), $"{version}_{table_name}.json.gz");
		}

		private string GetVersionsUrl()
		{
			return $"{TablesConfig.GetTablesPath(true)}version.json";
		}

		public void Reset()
		{
			DebugLogger.Log("[TablesLoader] Reset");

			StopLoading();
			
			_versions = null;
			_failed = false;
			_loadingTasks?.Clear();
		}

		public void Add<ItemType, WrapperType>(SnipeTable<ItemType> table, string name)
			where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
			where ItemType : SnipeTableItem, new()
		{
			_loadingTasks ??= new List<Func<CancellationToken, Task>>();
			_loadingTasks.Add((CancellationToken cancellationToken) =>
			{
				return Load<ItemType, WrapperType>(table, name, cancellationToken);
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
				await LoadVersion(cancellationToken);

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

		private async Task<bool> LoadVersion(CancellationToken cancellation_token)
		{
			for (int retries_count = 0; retries_count < 3; retries_count++)
			{
				string url = GetVersionsUrl();

				DebugLogger.Log($"[TablesLoader] LoadVersion ({retries_count}) " + url);

				try
				{
					using (var loader = new HttpClient())
					{
						loader.Timeout = TimeSpan.FromSeconds(1);

						var load_task = loader.GetAsync(url); // , cancellation_token);
						if (await Task.WhenAny(load_task, Task.Delay(1000, cancellation_token)) == load_task)
						{
							if (load_task.Result.IsSuccessStatusCode)
							{
								var json = await load_task.Result.Content.ReadAsStringAsync();
								_versions = ParseVersionsJson(json);

								if (_versions == null)
								{
									Analytics.TrackEvent($"Tables - LoadVersion Failed to prase versions json", new SnipeObject()
									{
										["url"] = url,
										["json"] = json,
									});
								}
								else
								{
									DebugLogger.Log($"[TablesLoader] LoadVersion done - {_versions.Count} items");
								}
								break;
							}
							else
							{
								Analytics.TrackEvent($"Tables - LoadVersion Failed to load url", new SnipeObject()
								{
									["HttpStatus"] = load_task.Result.StatusCode,
									["HttpStatusCode"] = (int)load_task.Result.StatusCode,
									["url"] = url,
								});

								if (load_task.Result.StatusCode == System.Net.HttpStatusCode.NotFound)
								{
									// HTTP Status: 404
									// It is useless to retry loading
									DebugLogger.Log($"[TablesLoader] LoadVersion StatusCode = {load_task.Result.StatusCode} - will not rety");
									return false;
								}
							}
						}
					}
				}
				catch (Exception e) when (e is AggregateException ae && ae.InnerException is HttpRequestException)
				{
					DebugLogger.Log($"[TablesLoader] LoadVersion HttpRequestException - network is unreachable - will not rety. {e}");
					return false;
				}
				catch (Exception e) when (e is OperationCanceledException ||
						e is AggregateException ae && ae.InnerException is OperationCanceledException)
				{
					DebugLogger.Log($"[TablesLoader] LoadVersion - TaskCanceled");
					return false;
				}
				catch (Exception e)
				{
					DebugLogger.Log($"[TablesLoader] LoadVersion - Exception: {e}");
				}

				if (cancellation_token.IsCancellationRequested)
				{
					DebugLogger.Log($"[TablesLoader] LoadVersion task canceled");
					return false;
				}

				try
				{
					await Task.Delay(500, cancellation_token);
				}
				catch (OperationCanceledException e)
				{
					DebugLogger.Log($"[TablesLoader] LoadVersion task canceled");
					return false;
				}
			}

			if (_versions == null)
			{
				DebugLogger.Log($"[TablesLoader] LoadVersion Failed");
				Analytics.TrackEvent("Tables - LoadVersion Failed");
				return false;
			}

			return true;
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

			DebugLogger.Log("[TablesLoader] Faield to prase versions json");
			return null;
		}

		private async Task Load<ItemType, WrapperType>(SnipeTable<ItemType> table, string name, CancellationToken cancellationToken)
			where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
			where ItemType : SnipeTableItem, new()
		{
			_semaphore ??= new SemaphoreSlim(MAX_LOADERS_COUNT);

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
					loaded = await table.LoadAsync<WrapperType>(name, version, cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
				cancelled = true;
			}
			catch (Exception e)
			{
				exception = e;
				DebugLogger.Log($"[TablesLoader] Load {name} - Exception: {e}");
			}
			finally
			{
				_semaphore.Release();
			}

			if (!loaded && !_failed)
			{
				_failed = true;
				DebugLogger.LogWarning($"[TablesLoader] Loading failed: {name}. StopLoading.");

				if (!cancelled)
				{
					Analytics.TrackError($"Tables - Failed to load table '{name}'", exception);
				}

				StopLoading();
			}
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
					DebugLogger.Log($"[TablesLoader] RemoveMisversionedCache - Delete {filePath}");
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
						DebugLogger.Log($"[TablesLoader] RemoveOutdatedCache - Delete {filePath}");
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