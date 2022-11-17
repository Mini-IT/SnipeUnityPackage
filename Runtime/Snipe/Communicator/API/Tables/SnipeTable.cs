using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class SnipeTable
	{
		protected static readonly TablesLoader _loader = new TablesLoader();

		protected static readonly object _cacheIOLock = new object();

		public bool Loaded { get; protected set; } = false;
		public bool LoadingFailed { get; protected set; } = false;

		public enum LoadingLocation
		{
			Network,  // External URL
			Cache,    // Application cache
			BuiltIn,  // StremingAssets
		}
		public LoadingLocation LoadedFrom { get; protected set; } = LoadingLocation.Network;

		public static void Initialize()
		{
			TablesLoader.Initialize();
		}

		public static void ResetVersion()
		{
			_loader.Reset();
		}
	}

	public class SnipeTable<ItemType> : SnipeTable where ItemType : SnipeTableItem, new()
	{
		public event Action<bool> LoadingFinished;

		public Dictionary<int, ItemType> Items { get; private set; }

		public ItemType this[int id]
		{
			get
			{
				TryGetValue(id, out var item);
				return item;
			}
		}

		public bool TryGetValue(int id, out ItemType item)
		{
			if (Loaded && Items != null)
			{
				return Items.TryGetValue(id, out item);
			}

			item = default;
			return false;
		}

		private static string GetCachePath(string table_name, string version)
		{
			return Path.Combine(SnipeConfig.PersistentDataPath, $"{version}_{table_name}.json.gz");
		}

		private static string GetBuiltInPath(string table_name, string version)
		{
			// NOTE: There is a bug - only lowercase works
			// (https://issuetracker.unity3d.com/issues/android-loading-assets-from-assetbundles-takes-significantly-more-time-when-the-project-is-built-as-an-aab)
			return $"{version}_{table_name}.jsongz".ToLower();
		}

		private static string GetTableUrl(string table_name)
		{
			return $"{SnipeConfig.GetTablesPath()}{table_name}.json.gz";
		}

		public async Task Load<WrapperType>(string table_name) where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
		{
			if (Loaded)
				return;

			Items = new Dictionary<int, ItemType>();

			await _loader.Load<ItemType, WrapperType>(this, table_name);
		}

		internal async Task<bool> LoadAsync<WrapperType>(string table_name, string version, CancellationToken cancellation) where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
		{
			if (cancellation.IsCancellationRequested)
			{
				DebugLogger.Log($"[SnipeTable] Failed to load table - {table_name}   (task canceled)");
				return false;
			}

			DebugLogger.Log($"[SnipeTable] LoadTask start - {table_name}");

			// Try to load from cache
			if (!string.IsNullOrEmpty(version))
			{
				string cache_path = GetCachePath(table_name, version);
				ReadFile<WrapperType>(table_name, cache_path);

				// If loading from cache failed
				// try to load built-in file
				if (!this.Loaded)
				{
					ReadFromStramingAssets<WrapperType>(table_name, GetBuiltInPath(table_name, version));
				}
			}

			// If loading from cache failed
			if (!this.Loaded)
			{
				string url = GetTableUrl(table_name);
				DebugLogger.Log("[SnipeTable] Loading table " + url);

				this.LoadingFailed = false;
				using (var loader = new HttpClient())
				{
					int retry = 0;
					while (!this.Loaded && retry <= 2)
					{
						if (cancellation.IsCancellationRequested)
						{
							DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (task canceled)");
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
							var loader_task = loader.GetAsync(url, cancellation);
							Task finished_task = await Task.WhenAny(loader_task, Task.Delay(3000, cancellation));
							
							if (cancellation.IsCancellationRequested)
							{
								DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (task canceled)");
								return false;
							}

							if (finished_task != loader_task)
							{
								DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (timeout)");
								return false;
							}

							if (loader_task.IsFaulted || loader_task.IsCanceled || loader_task.Result == null || !loader_task.Result.IsSuccessStatusCode)
							{
								DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (loader failed)");
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
									ReadGZip<WrapperType>(file_content_stream);
								}

								if (this.Loaded)
								{
									DebugLogger.Log("[SnipeTable] Table ready - " + table_name);

									// "using" block in ReadGZip closes the stream. We need to open it again
									using (var file_content_stream = await response.Content.ReadAsStreamAsync())
									{
										SaveToCache(file_content_stream, table_name, version);
									}
								}
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeTable] Failed to parse table - {table_name} - {e}");
							}
						}
					}
				}
			}

			this.LoadingFailed = !this.Loaded;
			LoadingFinished?.Invoke(this.Loaded);
			return this.Loaded;
		}

		private void ReadFile<WrapperType>(string table_name, string file_path) where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
		{
			lock (_cacheIOLock)
			{
				if (File.Exists(file_path))
				{
					using (var read_stream = new FileStream(file_path, FileMode.Open))
					{
						try
						{
							ReadGZip<WrapperType>(read_stream);
						}
						catch (Exception)
						{
							DebugLogger.Log($"[SnipeTable] Failed to read file - {table_name}");
						}

						if (this.Loaded)
						{
							this.LoadedFrom = LoadingLocation.Cache;
							DebugLogger.Log($"[SnipeTable] Table ready (from cache) - {table_name}");
						}
					}
				}
			}
		}

		private void ReadFromStramingAssets<WrapperType>(string table_name, string file_path) where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
		{
			DebugLogger.Log($"[SnipeTable] ReadFromStramingAssets - {file_path}");

			if (!BetterStreamingAssets.FileExists(file_path))
			{
				DebugLogger.Log($"[SnipeTable] ReadFromStramingAssets - file not found");
				return;
			}

			byte[] data = BetterStreamingAssets.ReadAllBytes(file_path);

			if (data != null)
			{
				using (var read_stream = new MemoryStream(data))
				{
					try
					{
						ReadGZip<WrapperType>(read_stream);
					}
					catch (Exception e)
					{
						DebugLogger.Log($"[SnipeTable] Failed to read file - {table_name} - {e}");
					}
				}

				if (this.Loaded)
				{
					this.LoadedFrom = LoadingLocation.BuiltIn;
					DebugLogger.Log($"[SnipeTable] Table ready (built-in) - {table_name}");
				}
			}
		}

		private void SaveToCache(Stream stream, string table_name, string version)
		{
			string cache_path = GetCachePath(table_name, version);

			lock (_cacheIOLock)
			{
				try
				{
					if (!File.Exists(cache_path))
					{
						DebugLogger.Log("[SnipeTable] Save to cache " + cache_path);

						using (FileStream cache_write_stream = new FileStream(cache_path, FileMode.Create, FileAccess.Write))
						{
							stream.Position = 0;
							stream.CopyTo(cache_write_stream);
						}
					}
				}
				catch (Exception e)
				{
					DebugLogger.Log("[SnipeTable] Failed to save to cache - " + table_name + " - " + e.Message);
				}
			}
		}

		private void ReadGZip<WrapperType>(Stream stream) where WrapperType : class, ISnipeTableItemsListWrapper<ItemType>, new()
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json_string = reader.ReadToEnd();

					WrapperType list_wrapper = default;
					var type_of_wrapper = typeof(WrapperType);

					if (type_of_wrapper == typeof(SnipeTableLogicItemsWrapper))
					{
						DebugLogger.Log("[SnipeTable] SnipeTableLogicItemsWrapper");

						list_wrapper = ParseListWrapper(json_string, SnipeTableLogicItemsWrapper.FromTableData) as WrapperType;
					}
					else if (type_of_wrapper == typeof(SnipeTableCalendarItemsWrapper))
					{
						DebugLogger.Log("[SnipeTable] SnipeTableCalendarItemsWrapper");

						list_wrapper = ParseListWrapper(json_string, SnipeTableCalendarItemsWrapper.FromTableData) as WrapperType;
					}
					else
					{
						//lock (_parseJSONLock)
						{
							list_wrapper = fastJSON.JSON.ToObject<WrapperType>(json_string);
						}
					}

					if (list_wrapper?.list != null)
					{
						foreach (ItemType item in list_wrapper.list)
						{
							Items[item.id] = item;
						}
					}

					this.Loaded = true;
				}
			}
		}

		private ISnipeTableItemsListWrapper ParseListWrapper(string json_string, Func<Dictionary<string, object>, ISnipeTableItemsListWrapper> parse_func)
		{
			Dictionary<string, object> parsed_data = null;
			//lock (_parseJSONLock)
			{
				parsed_data = SnipeObject.FromJSONString(json_string);
			}

			var list_wrapper = parse_func.Invoke(parsed_data);

			if (list_wrapper == null)
			{
				DebugLogger.Log("[SnipeTable] parsed_data is null");
			}

			return list_wrapper;
		}
	}
}