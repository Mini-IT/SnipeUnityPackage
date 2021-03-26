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
		
		protected static string mVersion = null;
		protected static bool mVersionRequested = false;
		protected static List<CancellationTokenSource> mCancellations;
		
		public static void ResetVersion()
		{
			DebugLogger.Log("[SnipeTable] ResetVersion");
			
			if (mCancellations != null)
			{
				// clone the list for thread safety
				var cancellations = new List<CancellationTokenSource>(mCancellations);
				foreach (var cancellation in cancellations)
				{
					cancellation?.Cancel();
				}
				mCancellations.Clear();
			}
			
			mVersion = null;
			mVersionRequested = false;
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
		
		protected static bool mVersionLoadingFinished = false;
		private static readonly object mCacheIOLocker = new object();
		private static readonly object mParseJSONLocker = new object();
		
		//private static SemaphoreSlim mSemaphore;
		
		public async Task Load(string table_name)
		{
			mLoadingCancellation?.Cancel();
			mLoadingCancellation = new CancellationTokenSource();
			
			if (mCancellations == null)
				mCancellations = new List<CancellationTokenSource>();
			mCancellations.Add(mLoadingCancellation);
			
			try
			{
				await LoadAsync(table_name, mLoadingCancellation.Token);
			}
			finally
			{
				mCancellations.Remove(mLoadingCancellation);
			}
		}
		
		private async Task LoadAsync(string table_name, CancellationToken cancellation_token)
		{
			if (!mVersionRequested)
			{
				mVersionRequested = true;
				mVersionLoadingFinished = false;
				
				BetterStreamingAssets.Initialize();
				
				string url = $"{SnipeConfig.Instance.GetTablesPath()}version.txt";
				DebugLogger.Log("[SnipeTable] LoadVersion " + url);
				
				try
				{
					var load_task = new HttpClient().GetStringAsync(url); // , cancellation_token);
					if (await Task.WhenAny(load_task, Task.Delay(10000, cancellation_token)) == load_task)
					{
						var content = load_task.Result;
						mVersion = content.Trim();
						
						mVersionLoadingFinished = true;
						DebugLogger.Log($"[SnipeTable] LoadVersion done - {mVersion}");
					}
					else // timeout
					{
						DebugLogger.Log($"[SnipeTable] LoadVersion - failed by timeout");
						//mVersionLoadingFailed = true;
						return;
					}
				}
				// catch (TaskCanceledException)
				// {
					// DebugLogger.Log($"[SnipeTable] LoadVersion - TaskCanceled");
				// }
				catch (Exception e)
				{
					DebugLogger.Log($"[SnipeTable] LoadVersion - Exception: {e.Message}");
					return;
				}
			}
			else
			{
				while (string.IsNullOrEmpty(mVersion))
				{
					if (cancellation_token.IsCancellationRequested)
					{
						return;
					}
					
					await Task.Yield();
				}
			}
			
			// if (mSemaphore == null)
			// {
				// mSemaphore = new SemaphoreSlim(MAX_LOADERS_COUNT);
				
				// BetterStreamingAssets.Initialize();
			// }
			
			Loaded = false;
			Items = new Dictionary<int, ItemType>();
			
			try
			{
				//await mSemaphore.WaitAsync(cancellation_token);
				await LoadTask(table_name, cancellation_token);
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeTable] Load {table_name} - Exception: {e.Message}");
			}
			// finally
			// {
				// mSemaphore.Release();
			// }
		}

		protected string GetCachePath(string table_name)
		{
			return Path.Combine(SnipeConfig.Instance.PersistentDataPath, $"{mVersion}_{table_name}.json.gz");
		}
		
		protected string GetBuiltInPath(string table_name)
		{
			// NOTE: There is a bug - only lowercase works
			// (https://issuetracker.unity3d.com/issues/android-loading-assets-from-assetbundles-takes-significantly-more-time-when-the-project-is-built-as-an-aab)
			return $"{mVersion}_{table_name}.jsongz".ToLower();
		}
		
		protected string GetTableUrl(string table_name)
		{
			return $"{SnipeConfig.Instance.GetTablesPath()}{table_name}.json.gz";
		}
		
		private async Task LoadTask(string table_name, CancellationToken cancellation)
		{
			if (cancellation.IsCancellationRequested)
			{
				DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (task canceled)");
				return;
			}
			
			DebugLogger.Log($"[SnipeTable] LoadTask start - {table_name}");

			// Try to load from cache
			if (!string.IsNullOrEmpty(mVersion))
			{
				string cache_path = GetCachePath(table_name);
				ReadFile(table_name, cache_path);
				
				// If loading from cache failed
				// try to load built-in file
				if (!this.Loaded)
				{
					ReadFromStramingAssets(table_name, GetBuiltInPath(table_name));
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
							DebugLogger.Log("[SnipeTable] Table ready - " + table_name);
							
							// "using" block in ReadGZip closes the stream. We need to open it again
							using (var file_content_stream = await loader_task.Result.Content.ReadAsStreamAsync())
							{
								SaveToCache(file_content_stream, table_name);
							}
						}
					}
					catch (Exception e)
					{
						DebugLogger.Log($"[SnipeTable] Failed to parse table - {table_name}  {e.Message} {e.StackTrace}");
					}
				}
			}

			this.LoadingFailed = !this.Loaded;
			LoadingFinished?.Invoke(this.Loaded);
		}
		
		private void ReadFile(string table_name, string file_path)
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
							DebugLogger.Log($"[SnipeTable] Failed to read file - {table_name}");
						}

						if (this.Loaded)
						{
							DebugLogger.Log($"[SnipeTable] Table ready (from cache) - {table_name}");
						}
					}
				}
			}
		}

		private void ReadFromStramingAssets(string table_name, string file_path)
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
						DebugLogger.Log($"[SnipeTable] Failed to read file - {table_name} - {e.Message}");
					}
				}
				
				if (this.Loaded)
				{
					DebugLogger.Log($"[SnipeTable] Table ready (built-in) - {table_name}");
					
#if UNITY_ANDROID
					// "using" block in ReadGZip closes the stream. We need to open it again
					using (var read_stream = new MemoryStream(data))
					{
						SaveToCache(read_stream, table_name);
					}
#endif
				}
			}
		}
		
		private void SaveToCache(Stream stream, string table_name)
		{
			string cache_path = GetCachePath(table_name);
			
			lock (mCacheIOLocker)
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

		private void ReadGZip(Stream stream)
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json_string = reader.ReadToEnd();
					
					System.Diagnostics.Stopwatch sw = null;
					SnipeObject data = null;
					lock (mParseJSONLocker)
					{
						sw = System.Diagnostics.Stopwatch.StartNew();
						//long mem_parse_fast = GC.GetTotalMemory(false);
						data = SnipeObject.FromFastJSONString(json_string);
						//mem_parse_fast = GC.GetTotalMemory(false) - mem_parse_fast;
						sw.Stop();
					}
					
					// var sw_old = System.Diagnostics.Stopwatch.StartNew();
					// long mem_parse_old = GC.GetTotalMemory(false);
					// SnipeObject data_old = SnipeObject.FromJSONString(json_string);
					// mem_parse_old = GC.GetTotalMemory(false) - mem_parse_old;
					// sw_old.Stop();
					
					float parse_time_fast = (float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond;
					// float parse_time_old = (float)sw_old.ElapsedTicks / TimeSpan.TicksPerMillisecond;
					
					sw = System.Diagnostics.Stopwatch.StartNew();
					if (data["list"] is List<object> list)
					{
						foreach (var item_data in list)
						{
							if (item_data is Dictionary<string, object> dict)
								AddTableItem(dict);
						}
					}
					sw.Stop();
					
					// sw_old = System.Diagnostics.Stopwatch.StartNew();
					// if (data_old["list"] is List<object> lst)
					// {
						// foreach (SnipeObject item_data in lst)
						// {
							// AddTableItem(item_data);
						// }
					// }
					// sw_old.Stop();
					
					float inst_time_fast = (float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond;
					//float inst_time_old = (float)sw_old.ElapsedTicks / TimeSpan.TicksPerMillisecond;
					
					DebugLogger.Log($"[SnipeTable] Parsing + Instantiating:  fast: {parse_time_fast} + {inst_time_fast} = {parse_time_fast + inst_time_fast}");
					//DebugLogger.Log($"[SnipeTable] Parsing + Instantiating:  old: {parse_time_old} + {inst_time_old} = {parse_time_old + inst_time_old}");
					//DebugLogger.Log($"[SnipeTable] Memory useage:  fast: {mem_parse_fast} old: {mem_parse_old}");

					this.Loaded = true;
				}
			}
		}
		
		protected void AddTableItem(Dictionary<string, object> item_data)
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