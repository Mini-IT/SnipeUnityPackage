using MiniIT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class SnipeTable
	{
		protected const int MAX_LOADERS_COUNT = 5;
		protected static List<string> mLoadingTables;
		
		protected static string mVersion = null;
		protected static bool mVersionRequested = false;
		protected static bool mVersionLoadingFailed = false;
		protected static WeakReference<CancellationTokenSource> mVersionLoadingCancellation;
		
		public static void ResetVersion()
		{
			if (mVersionLoadingCancellation != null && mVersionLoadingCancellation.TryGetTarget(out var cancellation))
			{
				cancellation.Cancel();
				mVersionLoadingCancellation = null;
			}
			mVersion = null;
			mVersionRequested = false;
			mVersionLoadingFailed = false;
		}
		
		protected async Task LoadVersion(CancellationTokenSource cancellation_source)
		{
			mVersionRequested = true;
			
			string url = $"{SnipeConfig.Instance.GetTablesPath()}/version.txt";
			
			DebugLogger.Log("[SnipeTable] LoadVersion " + url);
			
			try
			{
				var loader = new HttpClient();
				
				mVersionLoadingCancellation = new WeakReference<CancellationTokenSource>(cancellation_source);
				CancellationToken cancellation = cancellation_source.Token;
				var loader_task = loader.GetAsync(url, cancellation);

				await loader_task;

				if (cancellation.IsCancellationRequested)
				{
					DebugLogger.Log("[SnipeTable] LoadVersion - Failed to load tables version - (task canceled)");
					mVersionRequested = false;
					return;
				}

				if (loader_task.IsFaulted || loader_task.IsCanceled)
				{
					DebugLogger.Log("[SnipeTable] LoadVersion - Failed to load tables version - (loader failed)");
					mVersionLoadingFailed = true;
					return;
				}
				
				DebugLogger.Log("[SnipeTable] LoadVersion - file loaded");
				
				using (var reader = new StreamReader(await loader_task.Result.Content.ReadAsStreamAsync()))
				{
					mVersion = reader.ReadLine().Trim();
					DebugLogger.Log($"[SnipeTable] LoadVersion done - {mVersion}");
				}
			}
			catch (Exception)
			{
				mVersionLoadingFailed = true;
				DebugLogger.Log("[SnipeTable] LoadVersion - Failed to read tables version");
			}
		}
	}
	
	public class SnipeTable<ItemType> : SnipeTable where ItemType : SnipeTableItem, new()
	{
		public delegate void LoadingFinishedHandler(bool success);
		public event LoadingFinishedHandler LoadingFinished;

		public bool Loaded { get; private set; } = false;
		public bool LoadingFailed { get; private set; } = false;
		
		public Dictionary<int, ItemType> Items { get; private set; }
		
		private CancellationTokenSource mLoadingCancellation;
		
		private static readonly object mCacheIOLocker = new object();
		
		public void Load(string table_name)
		{
			DebugLogger.Log("[SnipeTable] Load - " + table_name);
			Task.Run(async () => await LoadAsync(table_name));
		}
		
		private async Task LoadAsync(string table_name)
		{
			mLoadingCancellation?.Cancel();
			mLoadingCancellation = new CancellationTokenSource();
			
			if (!mVersionRequested)
			{
				mVersionRequested = true;
				await LoadVersion(mLoadingCancellation);
			}
			else
			{
				while (!mVersionLoadingFailed && string.IsNullOrEmpty(mVersion))
				{
					await Task.Delay(50, mLoadingCancellation.Token);
				}
			}
			
			Loaded = false;
			Items = new Dictionary<int, ItemType>();
			
#if UNITY_ANDROID && !UNITY_EDITOR
			BetterStreamingAssets.Initialize();
#endif
			
			if (mLoadingTables == null)
				mLoadingTables = new List<string>(MAX_LOADERS_COUNT);

//#pragma warning disable 4014
			try
			{
				await LoadTask(table_name, mLoadingCancellation.Token);
			}
			catch (TaskCanceledException)
			{
				DebugLogger.Log($"[SnipeTable] Load {table_name} - TaskCanceled");
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeTable] Load {table_name} - Exception: {e.Message}\n{e.StackTrace}");
			}
			finally
			{
				lock (mLoadingTables)
				{
					mLoadingTables.Remove(table_name);
				}
			}
//#pragma warning restore 4014
		}

		protected string GetCachePath(string table_name)
		{
			return Path.Combine(SnipeConfig.Instance.PersistentDataPath, $"{mVersion}_{table_name}.json.gz");
		}
		
		protected string GetBuiltInPath(string table_name)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			// NOTE: There is a bug - only lowercase works
			// (https://issuetracker.unity3d.com/issues/android-loading-assets-from-assetbundles-takes-significantly-more-time-when-the-project-is-built-as-an-aab)
			string filename = $"/{mVersion}_{table_name}.jsongz".ToLower();
			//return (SnipeConfig.Instance.StreamingAssetsPath + filename);
			return filename;
#else
			return Path.Combine(SnipeConfig.Instance.StreamingAssetsPath, $"{mVersion}_{table_name}.json.gz");
#endif
		}
		
		protected string GetTableUrl(string table_name)
		{
			return $"{SnipeConfig.Instance.GetTablesPath()}/{table_name}.json.gz";
		}
		
		private async Task LoadTask(string table_name, CancellationToken cancellation)
		{
			int loading_tables_count = 0;
			lock (mLoadingTables)
			{
				mLoadingTables.Remove(table_name);
				loading_tables_count = mLoadingTables.Count;
			}
			
			while (loading_tables_count >= MAX_LOADERS_COUNT)
			{
				await Task.Delay(20, cancellation);

				lock (mLoadingTables)
				{
					loading_tables_count = mLoadingTables.Count;
				}
			}
			
			if (cancellation.IsCancellationRequested)
			{
				DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (task canceled)");
				return;
			}

			lock (mLoadingTables)
			{
				mLoadingTables.Add(table_name);
			}
			
			string cache_path = GetCachePath(table_name);
			
			// Try to load from cache
			if (!string.IsNullOrEmpty(mVersion))
			{
				ReadFile(table_name, cache_path, "from cache");
				
				// If loading from cache failed
				// try to load built-in file
				if (!this.Loaded)
				{
#if UNITY_ANDROID && !UNITY_EDITOR
					ReadFromStramingAssets(table_name, GetBuiltInPath(table_name), "built-in");
#else
					ReadFile(table_name, GetBuiltInPath(table_name), "built-in");
#endif
				}
			}
			
			// If loading from cache failed
			if (!this.Loaded)
			{
				string url = GetTableUrl(table_name);
				DebugLogger.Log("[SnipeTable] Loading table " + url);

				this.LoadingFailed = false;

				int retry = 0;
				while (!this.Loaded && retry <= 2)
				{
					if (cancellation.IsCancellationRequested)
					{
						DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (task canceled)");
						return;
					}
					
					if (retry > 0)
					{
						await Task.Delay(100, cancellation);
						DebugLogger.Log($"[SnipeTable] Retry #{retry} to load table - {table_name}");
					}

					retry++;

					try
					{
						var loader = new HttpClient();
						var loader_task = loader.GetAsync(url, cancellation);

						await loader_task;

						if (cancellation.IsCancellationRequested)
						{
							DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (task canceled)");
							return;
						}

						if (loader_task.IsFaulted || loader_task.IsCanceled)
						{
							DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (loader failed)");
							return;
						}
						
						using (var file_content_stream = await loader_task.Result.Content.ReadAsStreamAsync())
						{
							ReadGZip(file_content_stream);
						}
							
						if (this.Loaded)
						{
							// "using" block in ReadGZip closes the stream. We need to open it again
							using (var file_content_stream = await loader_task.Result.Content.ReadAsStreamAsync())
							{
								lock (mCacheIOLocker)
								{
									DebugLogger.Log("[SnipeTable] Table ready - " + table_name);
									
									// Save to cache
									try
									{
										if (!File.Exists(cache_path))
										{
											DebugLogger.Log("[SnipeTable] Save to cache " + cache_path);
											
											using (FileStream cache_write_stream = new FileStream(cache_path, FileMode.Create, FileAccess.Write))
											{
												file_content_stream.Position = 0;
												file_content_stream.CopyTo(cache_write_stream);
											}
										}
									}
									catch (Exception ex)
									{
										DebugLogger.Log("[SnipeTable] Failed to save to cache - " + table_name + " - " + ex.Message);
									}
								}
							}
						}
					}
					catch (Exception)
					{
						DebugLogger.Log("[SnipeTable] Failed to parse table - " + table_name);
					}
				}
			}

			this.LoadingFailed = !this.Loaded;
			LoadingFinished?.Invoke(this.Loaded);
		}
		
		private void ReadFile(string table_name, string file_path, string log_marker)
		{
			lock (mCacheIOLocker)
			{
				if (File.Exists(file_path))
				{
					using (var read_stream = new FileStream(file_path, FileMode.Open))
					{
						try
						{
							ReadGZip(read_stream);
						}
						catch (Exception)
						{
							DebugLogger.Log($"[SnipeTable] Failed to read {log_marker} - {table_name}");
						}

						if (this.Loaded)
						{
							DebugLogger.Log($"[SnipeTable] Table ready ({log_marker}) - {table_name}");
						}
					}
				}
			}
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private void ReadFromStramingAssets(string table_name, string file_path, string log_marker)
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
						ReadGZip(read_stream);
					}
					catch (Exception e)
					{
						DebugLogger.Log($"[SnipeTable] Failed to read {log_marker} - {table_name} - {e.Message}");
					}

					if (this.Loaded)
					{
						DebugLogger.Log($"[SnipeTable] Table ready ({log_marker}) - {table_name}");
					}
				}
			}
		}
#endif

		private void ReadGZip(Stream stream)
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json_string = reader.ReadToEnd();
					SnipeObject data = SnipeObject.FromJSONString(json_string);

					if (data["list"] is List<object> list)
					{
						foreach (SnipeObject item_data in list)
						{
							AddTableItem(item_data);
						}
					}

					this.Loaded = true;
				}
			}
		}
		
		protected void AddTableItem(SnipeObject item_data)
		{
			var item = new ItemType();
			item.SetData(item_data);
			if (item.id > 0)
			{
				Items[item.id] = item;
			}
		}
	}
}