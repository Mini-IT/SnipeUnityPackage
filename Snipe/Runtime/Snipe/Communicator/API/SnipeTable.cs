﻿using MiniIT;
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
		
		public static void Initialize()
		{
			BetterStreamingAssets.Initialize();
		}
		
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
		
		private static SemaphoreSlim mSemaphore;
		
		public async Task Load<WrapperType>(string table_name) where WrapperType : ISnipeTableItemsListWrapper<ItemType>, new()
		{
			mLoadingCancellation?.Cancel();
			mLoadingCancellation = new CancellationTokenSource();
			
			if (mCancellations == null)
				mCancellations = new List<CancellationTokenSource>();
			mCancellations.Add(mLoadingCancellation);
			
			try
			{
				await LoadAsync<WrapperType>(table_name, mLoadingCancellation.Token);
			}
			finally
			{
				mCancellations.Remove(mLoadingCancellation);
			}
		}
		
		private async Task LoadAsync<WrapperType>(string table_name, CancellationToken cancellation_token) where WrapperType : ISnipeTableItemsListWrapper<ItemType>, new()
		{
			if (!mVersionRequested)
			{
				mVersionRequested = true;
				mVersionLoadingFinished = false;
				
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
			
			if (mSemaphore == null)
			{
				mSemaphore = new SemaphoreSlim(MAX_LOADERS_COUNT);
			}
			
			Loaded = false;
			Items = new Dictionary<int, ItemType>();
			
			try
			{
				await mSemaphore.WaitAsync(cancellation_token);
				await LoadTask<WrapperType>(table_name, cancellation_token);
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeTable] Load {table_name} - Exception: {e.Message}");
			}
			finally
			{
				mSemaphore.Release();
			}
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
		
		private async Task LoadTask<WrapperType>(string table_name, CancellationToken cancellation) where WrapperType : ISnipeTableItemsListWrapper<ItemType>, new()
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
				ReadFile<WrapperType>(table_name, cache_path);
				
				// If loading from cache failed
				// try to load built-in file
				if (!this.Loaded)
				{
					ReadFromStramingAssets<WrapperType>(table_name, GetBuiltInPath(table_name));
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
							ReadGZip<WrapperType>(file_content_stream);
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
		
		private void ReadFile<WrapperType>(string table_name, string file_path) where WrapperType : ISnipeTableItemsListWrapper<ItemType>, new()
		{
			lock (mCacheIOLocker)
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
							DebugLogger.Log($"[SnipeTable] Table ready (from cache) - {table_name}");
						}
					}
				}
			}
		}

		private void ReadFromStramingAssets<WrapperType>(string table_name, string file_path) where WrapperType : ISnipeTableItemsListWrapper<ItemType>, new()
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
						DebugLogger.Log($"[SnipeTable] Failed to read file - {table_name} - {e.Message}");
					}
				}
				
				if (this.Loaded)
				{
					DebugLogger.Log($"[SnipeTable] Table ready (built-in) - {table_name}");
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

		private void ReadGZip<WrapperType>(Stream stream) where WrapperType : ISnipeTableItemsListWrapper<ItemType>, new()
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json_string = reader.ReadToEnd();
					
					WrapperType list_wrapper;
					lock (mParseJSONLocker)
					{
						var sw = System.Diagnostics.Stopwatch.StartNew();
						list_wrapper = fastJSON.JSON.ToObject<WrapperType>(json_string);
						sw.Stop();
						
						float parse_time_fast = (float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond;
						DebugLogger.Log($"[SnipeTable] Parsing {parse_time_fast} ms");
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
	}
}