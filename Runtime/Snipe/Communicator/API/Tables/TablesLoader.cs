using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class TablesLoader
	{
		private const int MAX_LOADERS_COUNT = 4;
		private const string VERSION_FILE_NAME = "snipe_tables_version.txt";

		private string _version = null;

		private CancellationTokenSource _cancellation;
		private SemaphoreSlim _semaphore;
		private bool _forceBuiltInVersion = false;

		public static void Initialize()
		{
			BetterStreamingAssets.Initialize();
		}

		public void Reset()
		{
			DebugLogger.Log("[TablesLoader] Reset");

			StopLoading();
			
			_version = null;
			_forceBuiltInVersion = false;
		}
		
		public async Task Load<ItemType, WrapperType>(SnipeTable<ItemType> table, string name)
			where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
			where ItemType : SnipeTableItem, new()
		{
			if (_cancellation == null)
			{
				_cancellation = new CancellationTokenSource();
				await LoadVersion(_cancellation.Token);
			}

			while (_cancellation != null && !_cancellation.IsCancellationRequested && string.IsNullOrEmpty(_version))
			{
				await Task.Delay(50, _cancellation.Token);
			}

			if (_cancellation == null || _cancellation.IsCancellationRequested)
				return;

			_semaphore ??= new SemaphoreSlim(MAX_LOADERS_COUNT);

			bool loaded = false;

			try
			{
				await _semaphore.WaitAsync(_cancellation.Token);
				loaded = await table.LoadAsync<WrapperType>(name, _version, _cancellation.Token);
			}
			catch (TaskCanceledException)
			{
				// ignore
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[TablesLoader] Load {name} - Exception: {e}");
			}
			finally
			{
				_semaphore.Release();
			}

			if (!loaded && _cancellation != null)
			{
				DebugLogger.LogWarning($"[TablesLoader] Loading failed. Force loading built-in tables.");
				StopLoading();
				DeleteCahceVersionFile();
				_version = null;
				_forceBuiltInVersion = true;
				await Load<ItemType, WrapperType>(table, name);
			}
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

		private async Task<bool> LoadVersion(CancellationToken cancellation_token)
		{
			string version_file_path = GetCacheVersionFilePath();

			if (SnipeConfig.TablesUpdateEnabled && !_forceBuiltInVersion)
			{
				for (int retries_count = 0; retries_count < 3; retries_count++)
				{
					string url = $"{SnipeConfig.GetTablesPath(true)}version.txt";
					DebugLogger.Log($"[TablesLoader] LoadVersion ({retries_count}) " + url);

					try
					{
						using (var loader = new HttpClient())
						{
							loader.Timeout = TimeSpan.FromSeconds(1);

							var load_task = loader.GetAsync(url); // , cancellation_token);
							if (await Task.WhenAny(load_task, Task.Delay(1000)) == load_task && load_task.Result.IsSuccessStatusCode)
							{
								var content = await load_task.Result.Content.ReadAsStringAsync();
								_version = content.Trim();

								DebugLogger.Log($"[TablesLoader] LoadVersion done - {_version}");

								// save to file
								File.WriteAllText(version_file_path, _version);

								break;
							}
						}

						await Task.Delay(100, cancellation_token);
					}
					catch (Exception e)
					{
						if (e is TaskCanceledException ||
							e is AggregateException ae && ae.InnerException is TaskCanceledException)
						{
							DebugLogger.Log($"[TablesLoader] LoadVersion - TaskCanceled");
						}
						else
						{
							DebugLogger.Log($"[TablesLoader] LoadVersion - Exception: {e}");
						}
					}

					if (cancellation_token.IsCancellationRequested)
					{
						DebugLogger.Log($"[TablesLoader] LoadVersion task canceled");
						return false;
					}
				}
			}

			if (string.IsNullOrEmpty(_version))
			{
				DebugLogger.Log($"[TablesLoader] LoadVersion - Failed to load from URL. Trying to read from cache");

				long builtin_version = 0;
				long cached_version = 0;
				string builtin_version_string = null;
				string cached_version_string = null;

				if (BetterStreamingAssets.FileExists(VERSION_FILE_NAME))
				{
					builtin_version_string = BetterStreamingAssets.ReadAllText(VERSION_FILE_NAME).Trim();
					if (long.TryParse(builtin_version_string, out builtin_version))
					{
						DebugLogger.Log($"[TablesLoader] LoadVersion - built-in value - {builtin_version_string}");
					}
					else
					{
						builtin_version = 0;
					}
				}

				if (File.Exists(version_file_path))
				{
					cached_version_string = File.ReadAllText(version_file_path).Trim();
					if (long.TryParse(cached_version_string, out cached_version))
					{
						DebugLogger.Log($"[TablesLoader] LoadVersion - cached value - {cached_version_string}");
					}
					else
					{
						cached_version = 0;
					}
				}

				if (builtin_version > 0 && builtin_version > cached_version)
				{
					_version = builtin_version_string;
				}
				else if (cached_version > 0)
				{
					_version = cached_version_string;
				}
			}

			if (string.IsNullOrEmpty(_version))
			{
				DebugLogger.Log($"[TablesLoader] LoadVersion Failed");
				return false;
			}

			return true;
		}

		private string GetCacheVersionFilePath()
		{
			return Path.Combine(SnipeConfig.PersistentDataPath, VERSION_FILE_NAME);
		}

		private void DeleteCahceVersionFile()
		{
			string version_file_path = GetCacheVersionFilePath();
			if (File.Exists(version_file_path))
			{
				File.Delete(version_file_path);
			}
		}
	}
}