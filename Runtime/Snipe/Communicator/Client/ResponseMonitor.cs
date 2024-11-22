using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;

namespace MiniIT.Snipe
{
	public class ResponseMonitor : IDisposable
	{
		private const int RESPONSE_MONITORING_MAX_DELAY = 3000; // ms
		
		internal struct ResponseMonitoringItem
		{
			public TimeSpan Time;
			public string MessageType;
		}
		
		private IStopwatch _stopwatch;
		private IDictionary<int, ResponseMonitoringItem> _items; // key is request_id
		
		private CancellationTokenSource _cancellation;

		private readonly SnipeAnalyticsTracker _analytics;

		public ResponseMonitor(SnipeAnalyticsTracker analytics)
		{
			_analytics = analytics;
			_stopwatch = SnipeServices.FuzzyStopwatchFactory.Create();
		}

		public void Add(int request_id, string message_type)
		{
			if (message_type == SnipeMessageTypes.USER_LOGIN)
			{
				return;
			}

			if (_stopwatch == null)
			{
				throw new ObjectDisposedException(GetType().Name);
			}
				
			_items ??= new ConcurrentDictionary<int, ResponseMonitoringItem>();
			_stopwatch.Restart();
			
			_items[request_id] = new ResponseMonitoringItem()
			{
				Time = _stopwatch.Elapsed,
				MessageType = message_type,
			};
			
			Start();
		}
		
		public void Remove(int requestID, string messageType)
		{
			if (_items == null)
			{
				return;
			}

			bool found = false;

			try
			{
				if (_items.TryGetValue(requestID, out var item))
				{
					found = true;

					if (item.MessageType != messageType)
					{
						_analytics.TrackEvent("Wrong response type", new SnipeObject()
							{
								["request_id"] = requestID,
								["request_type"] = item.MessageType,
								["response_type"] = messageType,
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
					_items.Remove(requestID);
				}
			}
		}
		
		public void Start()
		{
			if (_cancellation != null)
			{
				return;
			}
			
			_items?.Clear();
			
			_cancellation = new CancellationTokenSource();
			AlterTask.RunAndForget(() => ResponseMonitoring(_cancellation.Token));
		}

		public void Stop()
		{
			_items?.Clear();
			
			if (_cancellation != null)
			{
				_cancellation.Cancel();
				_cancellation = null;
			}
		}

		public void Dispose()
		{
			Stop();

			if (_stopwatch is IDisposable disposableStopwatch)
			{
				disposableStopwatch.Dispose();
			}

			_stopwatch = null;

			GC.SuppressFinalize(this);
		}

		private async void ResponseMonitoring(CancellationToken cancellation)
		{
			List<int> keysToRemove = null;

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
				
				keysToRemove?.Clear();
				
				if (_items != null && _stopwatch != null)
				{
					var now = _stopwatch.Elapsed;
					
					foreach (var pair in _items)
					{
						var requestID = pair.Key;
						var item = pair.Value;
						
						if (now.Subtract(item.Time).TotalMilliseconds > RESPONSE_MONITORING_MAX_DELAY)
						{
							keysToRemove ??= new List<int>();
							keysToRemove.Add(requestID);
							
							_analytics.TrackEvent("Response not found", new SnipeObject()
								{
									["request_id"] = requestID,
									["message_type"] = item.MessageType,
								});
						}
					}
					
					if (keysToRemove != null)
					{
						for (int i = 0; i < keysToRemove.Count; i++)
						{
							_items.Remove(keysToRemove[i]);
						}
					}
				}				
			}

			ILogger logger = SnipeServices.LogService.GetLogger(nameof(ResponseMonitor));
			logger.LogTrace("ResponseMonitoring - finish");
		}
	}
}
