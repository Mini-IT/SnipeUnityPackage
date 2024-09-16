using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;

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
		
		private Stopwatch _responseMonitoringStopwatch;
		private IDictionary<int, ResponseMonitoringItem> _responseMonitoringItems; // key is request_id
		
		private CancellationTokenSource _responseMonitoringCancellation;
		
		private void AddResponseMonitoringItem(int request_id, string message_type)
		{
			if (message_type == SnipeMessageTypes.USER_LOGIN)
				return;
				
			if (_responseMonitoringItems == null)
				_responseMonitoringItems = new ConcurrentDictionary<int, ResponseMonitoringItem>();
			if (_responseMonitoringStopwatch == null)
				_responseMonitoringStopwatch = Stopwatch.StartNew();
			
			_responseMonitoringItems[request_id] = new ResponseMonitoringItem()
			{
				time = _responseMonitoringStopwatch.ElapsedMilliseconds,
				message_type = message_type,
			};
			
			StartResponseMonitoring();
		}
		
		private void RemoveResponseMonitoringItem(int request_id, string message_type)
		{
			if (_responseMonitoringItems == null)
			{
				return;
			}

			bool found = false;

			try
			{
				if (_responseMonitoringItems.TryGetValue(request_id, out var item))
				{
					found = true;

					if (item != null && item.message_type != message_type)
					{
						_analytics.TrackEvent("Wrong response type", new SnipeObject()
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
				if (found)
				{
					_responseMonitoringItems.Remove(request_id);
				}
			}
		}
		
		private void StartResponseMonitoring()
		{
			if (_responseMonitoringCancellation != null)
				return;
			
			if (_responseMonitoringItems != null)
				_responseMonitoringItems.Clear();
			
			_responseMonitoringCancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => ResponseMonitoring(_responseMonitoringCancellation.Token));
		}

		private void StopResponseMonitoring()
		{
			if (_responseMonitoringItems != null)
				_responseMonitoringItems.Clear();
			
			if (_responseMonitoringCancellation != null)
			{
				_responseMonitoringCancellation.Cancel();
				_responseMonitoringCancellation = null;
			}
		}

		private async void ResponseMonitoring(CancellationToken cancellation)
		{
			List<int> keys_to_remove = null;

			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				try
				{
					await AlterTask.Delay(1000, cancellation);
				}
				catch (OperationCanceledException)
				{
					// This is OK. Just terminating the task
					return;
				}
				
				keys_to_remove?.Clear();
				
				if (_responseMonitoringItems != null && _responseMonitoringStopwatch != null)
				{
					var time_now = _responseMonitoringStopwatch.ElapsedMilliseconds;
					
					foreach (var pair in _responseMonitoringItems)
					{
						var request_id = pair.Key;
						var item = pair.Value;
						
						if (time_now - item.time > RESPONSE_MONITORING_MAX_DELAY)
						{
							keys_to_remove ??= new List<int>();
							keys_to_remove.Add(request_id);
							
							_analytics.TrackEvent("Response not found", new SnipeObject()
								{
									["request_id"] = request_id,
									["message_type"] = item.message_type,
								});
						}
					}
					
					if (keys_to_remove != null)
					{
						for (int i = 0; i < keys_to_remove.Count; i++)
						{
							_responseMonitoringItems.Remove(keys_to_remove[i]);
						}
					}
				}				
			}
			
			_logger.LogTrace("ResponseMonitoring - finish");
		}
	}
}
