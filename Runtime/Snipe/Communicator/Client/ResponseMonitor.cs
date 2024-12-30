using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MiniIT.Threading;

namespace MiniIT.Snipe
{
	public class ResponseMonitor : IDisposable
	{
		private const int RESPONSE_MONITORING_MAX_DELAY = 3000; // ms

		private struct ResponseMonitoringItem
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

		public void Add(int requestID, string messageType)
		{
			if (messageType == SnipeMessageTypes.USER_LOGIN)
			{
				return;
			}

			if (_stopwatch == null)
			{
				throw new ObjectDisposedException(GetType().Name);
			}

			_items ??= new ConcurrentDictionary<int, ResponseMonitoringItem>(); // Concurrent ????????????
			_stopwatch.Restart();

			_items[requestID] = new ResponseMonitoringItem()
			{
				Time = _stopwatch.Elapsed,
				MessageType = messageType,
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
			AlterTask.RunAndForget(() => MonitoringLoop(_cancellation.Token));
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

		private async void MonitoringLoop(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested)
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

				if (_stopwatch == null)
				{
					break;
				}

				if (_items != null)
				{
					ProcessItems();
				}
			}
		}

		private void ProcessItems()
		{
			int removeCount = 0;
			Span<int> removeKeys = stackalloc int[_items.Count];

			var now = _stopwatch.Elapsed;

			foreach (var pair in _items)
			{
				int requestID = pair.Key;
				var item = pair.Value;

				TimeSpan timePassed = now.Subtract(item.Time);

				if (timePassed.TotalMilliseconds > RESPONSE_MONITORING_MAX_DELAY)
				{
					removeKeys[removeCount++] = requestID;

					_analytics.TrackEvent("Response not found", new SnipeObject()
					{
						["request_id"] = requestID,
						["message_type"] = item.MessageType,
					});
				}
			}

			for (int i = 0; i < removeCount; i++)
			{
				_items.Remove(removeKeys[i]);
			}
		}
	}
}
