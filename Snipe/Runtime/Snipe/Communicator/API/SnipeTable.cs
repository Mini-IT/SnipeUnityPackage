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
	}
	
	public class SnipeTable<ItemType> : SnipeTable where ItemType : SnipeTableItem, new()
	{
		public delegate void LoadingFinishedHandler(bool success);
		public event LoadingFinishedHandler LoadingFinished;

		public bool Loaded { get; private set; } = false;
		public bool LoadingFailed { get; private set; } = false;
		
		public Dictionary<int, ItemType> Items { get; private set; }
		
		private CancellationTokenSource mLoadingCancellation;
		
		public async void Load(string table_name)
		{
			mLoadingCancellation?.Cancel();
			
			Items = new Dictionary<int, ItemType>();
			
			if (mLoadingTables == null)
				mLoadingTables = new List<string>(MAX_LOADERS_COUNT);
			
			mLoadingCancellation = new CancellationTokenSource();
			await LoadTask(table_name, mLoadingCancellation.Token);
			mLoadingTables.Remove(table_name);
		}

		private async Task LoadTask(string table_name, CancellationToken cancellation)
		{
			mLoadingTables.Remove(table_name);
			
			while (mLoadingTables.Count >= MAX_LOADERS_COUNT)
				await Task.Delay(20, cancellation);
			
			mLoadingTables.Add(table_name);

			string url = string.Format("{0}/{1}.json.gz", SnipeConfig.Instance.GetTablesPath(), table_name);
			DebugLogger.Log("[SnipeTable] Loading table " + url);

			this.LoadingFailed = false;

			int retry = 0;
			while (!this.Loaded && retry <= 2)
			{
				if (retry > 0)
				{
					await Task.Delay(100, cancellation);
					DebugLogger.Log($"[SnipeTable] Retry #{retry} to load table - {table_name}");
				}

				retry++;

				try
				{
					var loader = new HttpClient();

					var loader_cancellation = new CancellationTokenSource();
					var loader_task = loader.GetAsync(url, loader_cancellation.Token);

					if (await Task.WhenAny(loader_task, Task.Delay(5000, cancellation)) != loader_task)
					{
						DebugLogger.Log("[SnipeTable] Failed to load table - " + table_name + "   (timeout)");

						loader_cancellation.Cancel();
						continue;
					}

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

					using (GZipStream gzip = new GZipStream(loader_task.Result.Content.ReadAsStreamAsync().Result, CompressionMode.Decompress))
					{
						using (StreamReader reader = new StreamReader(gzip))
						{
							string json_string = reader.ReadToEnd();
							ExpandoObject data = ExpandoObject.FromJSONString(json_string);

							if (data["list"] is List<object> list)
							{
								foreach (ExpandoObject item_data in list)
								{
									AddTableItem(item_data);
								}
							}

							DebugLogger.Log("[SnipeTable] Table ready - " + table_name);
							this.Loaded = true;
						}
					}
				}
				catch (Exception)
				{
					DebugLogger.Log("[SnipeTable] Failed to parse table - " + table_name);
				}
			}

			this.LoadingFailed = !this.Loaded;
			LoadingFinished?.Invoke(this.Loaded);
		}
		
		protected void AddTableItem(ExpandoObject item_data)
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