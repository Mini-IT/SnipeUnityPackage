using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public partial class SnipeClient
	{
		private const int RESPONSE_MONITORING_MAX_DELAY = 3000; // ms
		
		class ResponseMonitoringItem
		{
			internal long time;
			internal string message_type;
		}
		
		private Stopwatch mResponseMonitoringStopwatch;
		private IDictionary<int, ResponseMonitoringItem> mResponseMonitoringItems; // key is request_id; value is time
		
		private CancellationTokenSource mResponseMonitoringCancellation;
		
		private void AddResponseMonitoringItem(int request_id, string message_type)
		{
			if (message_type == SnipeMessageTypes.USER_LOGIN)
				return;
				
			if (mResponseMonitoringItems == null)
				mResponseMonitoringItems = new Dictionary<int, ResponseMonitoringItem>();
			if (mResponseMonitoringStopwatch == null)
				mResponseMonitoringStopwatch = Stopwatch.StartNew();
			
			mResponseMonitoringItems[request_id] = new ResponseMonitoringItem()
			{
				time = mResponseMonitoringStopwatch.ElapsedMilliseconds,
				message_type = message_type,
			};
			
			StartResponseMonitoring();
		}
		
		private void RemoveResponseMonitoringItem(int request_id, string message_type)
		{
			try
			{
				if (mResponseMonitoringItems.TryGetValue(request_id, out var item) && item != null)
				{
					if (item.message_type != message_type)
					{
						Analytics.TrackEvent("Wrong response type", new SnipeObject()
							{
								["request_id"] = request_id,
								["request_type"] = item.message_type,
								["response_type"] = message_type,
							});
					}
				}
			}
			catch (Exception)
			{
				// ignore
			}
			finally
			{
				mResponseMonitoringItems?.Remove(request_id);
			}
		}
		
		private void StartResponseMonitoring()
		{
			if (mResponseMonitoringCancellation != null)
				return;
			
			if (mResponseMonitoringItems != null)
				mResponseMonitoringItems.Clear();
			
			mResponseMonitoringCancellation = new CancellationTokenSource();
			Task.Run(() => ResponseMonitoring(mResponseMonitoringCancellation.Token));
		}

		private void StopResponseMonitoring()
		{
			if (mResponseMonitoringItems != null)
				mResponseMonitoringItems.Clear();
			
			if (mResponseMonitoringCancellation != null)
			{
				mResponseMonitoringCancellation.Cancel();
				mResponseMonitoringCancellation = null;
			}
		}

		private async void ResponseMonitoring(CancellationToken cancellation)
		{
			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(1000, cancellation);
				}
				catch (TaskCanceledException)
				{
					// This is OK. Just terminating the task
					return;
				}
				
				if (mResponseMonitoringItems != null && mResponseMonitoringStopwatch != null)
				{
					var time_now = mResponseMonitoringStopwatch.ElapsedMilliseconds;
					
					var keys = new List<int>(mResponseMonitoringItems.Keys);
					for (int key_index = keys.Count - 1; key_index >= 0; key_index--)
					{
						var request_id = keys[key_index];
						var item = mResponseMonitoringItems[request_id];
						var request_time = item.time;
						
						if (time_now - request_time > RESPONSE_MONITORING_MAX_DELAY)
						{
							mResponseMonitoringItems.Remove(request_id);
							
							Analytics.TrackEvent("Response not found", new SnipeObject()
								{
									["request_id"] = request_id,
									["message_type"] = item.message_type,
								});
						}
					}
				}				
			}
			
			DebugLogger.Log("[SnipeClient] ResponseMonitoring - finish");
		}
	}
}