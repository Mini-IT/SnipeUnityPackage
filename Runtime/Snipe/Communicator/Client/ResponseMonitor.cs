using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class ResponseMonitor : IDisposable
	{
		private const int MAX_RESPONSE_MILLISECONDS = 3000;

		private struct ResponseMonitoringItem
		{
			public TimeSpan Time;
			public string MessageType;
		}

		private IStopwatch _stopwatch;

		private CancellationTokenSource _cancellation;

		private readonly IDictionary<int, ResponseMonitoringItem> _items;
		private readonly ISnipeAnalyticsTracker _analytics;

		public ResponseMonitor(ISnipeAnalyticsTracker analytics, ISnipeServices services)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			_analytics = analytics;

#if UNITY_WEBGL
			_items = new Dictionary<int, ResponseMonitoringItem>();
#else
			_items = new ConcurrentDictionary<int, ResponseMonitoringItem>();
#endif

			_stopwatch = services.FuzzyStopwatchFactory.Create();
			_stopwatch.Start();
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

			_items[requestID] = new ResponseMonitoringItem()
			{
				Time = _stopwatch.Elapsed,
				MessageType = messageType,
			};

			if (_cancellation == null)
			{
				// Start monitoring
				_cancellation = new CancellationTokenSource();
				AlterTask.RunAndForget(() => MonitoringLoop(_cancellation.Token));
			}
		}

		public void Remove(int requestID, string messageType)
		{
			bool found = false;

			try
			{
				if (_items.TryGetValue(requestID, out var item))
				{
					found = true;

					if (item.MessageType != messageType)
					{
						_analytics.TrackEvent("Wrong response type", new Dictionary<string, object>()
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

		public void Stop()
		{
			_items?.Clear();

			CancellationTokenHelper.CancelAndDispose(ref _cancellation);
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

				if (timePassed.TotalMilliseconds > MAX_RESPONSE_MILLISECONDS)
				{
					removeKeys[removeCount++] = requestID;

					_analytics.TrackEvent("Response not found", new Dictionary<string, object>()
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
