using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	internal sealed class SnipeRequestDispatcher : IDisposable
	{
		private const int RATE_LIMIT_INTERVAL_MS = 1000;

		private sealed class PendingSend
		{
			public IDictionary<string, object> Message;
			public List<IDictionary<string, object>> Batch;
			public bool AutoBatchAllowed;
		}

		private sealed class SentRequest
		{
			public int RequestId;
			public IDictionary<string, object> Message;
			public int NextRetryDelayMs = SnipeClient.RATE_LIMIT_RETRY_DELAY_MS;
			public bool RetryScheduled;
		}

		private readonly object _lock = new object();
		private readonly Queue<PendingSend> _pendingSends = new Queue<PendingSend>();
		private readonly Dictionary<int, SentRequest> _sentRequests = new Dictionary<int, SentRequest>();
		private readonly Func<IDictionary<string, object>, bool> _sendRequest;
		private readonly Func<List<IDictionary<string, object>>, bool> _sendBatch;
		private readonly Func<bool> _connected;
		private readonly Func<long> _getTimestamp;
		private readonly Func<int, CancellationToken, UniTask> _delay;
		private readonly long _timestampFrequency;
		private readonly ILogger _logger;

		private CancellationTokenSource _cancellation;
		private bool _drainStarted;
		private long _sendWindowStartTimestamp;
		private int _requestsSentInWindow;

		internal SnipeRequestDispatcher(
			Func<IDictionary<string, object>, bool> sendRequest,
			Func<List<IDictionary<string, object>>, bool> sendBatch,
			Func<bool> connected,
			ILogger logger)
			: this(sendRequest, sendBatch, connected, logger, Stopwatch.GetTimestamp, Stopwatch.Frequency, (t, c) => AlterTask.Delay(t, c).AsUniTask())
		{
		}

		internal SnipeRequestDispatcher(
			Func<IDictionary<string, object>, bool> sendRequest,
			Func<List<IDictionary<string, object>>, bool> sendBatch,
			Func<bool> connected,
			Func<long> getTimestamp,
			long timestampFrequency,
			Func<int, CancellationToken, UniTask> delay)
			: this(sendRequest, sendBatch, connected, EmptyLogger.Instance, getTimestamp, timestampFrequency, delay)
		{
		}

		internal SnipeRequestDispatcher(
			Func<IDictionary<string, object>, bool> sendRequest,
			Func<List<IDictionary<string, object>>, bool> sendBatch,
			Func<bool> connected,
			ILogger logger,
			Func<long> getTimestamp,
			long timestampFrequency,
			Func<int, CancellationToken, UniTask> delay)
		{
			_sendRequest = sendRequest ?? throw new ArgumentNullException(nameof(sendRequest));
			_sendBatch = sendBatch ?? throw new ArgumentNullException(nameof(sendBatch));
			_connected = connected ?? throw new ArgumentNullException(nameof(connected));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
			_timestampFrequency = timestampFrequency;
			_delay = delay ?? throw new ArgumentNullException(nameof(delay));
		}

		private sealed class EmptyLogger : ILogger
		{
			public static EmptyLogger Instance => s_instance ??= new EmptyLogger();
			private static EmptyLogger s_instance;

			public IDisposable BeginScope<TState>(TState state) => null;
			public bool IsEnabled(LogLevel logLevel) => false;
			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
		}

		public void Send(IDictionary<string, object> message, bool autoBatchAllowed)
		{
			if (message == null)
			{
				return;
			}

			var pendingSend = new PendingSend()
			{
				Message = message,
				AutoBatchAllowed = autoBatchAllowed,
			};

			Send(pendingSend);
		}

		public void SendBatch(List<IDictionary<string, object>> messages)
		{
			if (messages == null || messages.Count == 0)
			{
				return;
			}

			var pendingSend = new PendingSend()
			{
				Batch = messages,
			};

			Send(pendingSend);
		}

		public bool TryHandleRateLimit(int requestId)
		{
			SentRequest request;
			int delayMs;
			CancellationToken cancellation;

			lock (_lock)
			{
				if (!_sentRequests.TryGetValue(requestId, out request))
				{
					return false;
				}

				if (request.RetryScheduled)
				{
					return true;
				}

				request.RetryScheduled = true;
				delayMs = request.NextRetryDelayMs;
				request.NextRetryDelayMs = Math.Min(delayMs * 2, SnipeClient.MAX_RATE_LIMIT_RETRY_DELAY_MS);
				_cancellation ??= new CancellationTokenSource();
				cancellation = _cancellation.Token;
			}

			RetryRateLimitedRequest(request, delayMs, cancellation).Forget();
			return true;
		}

		public void RemoveSent(int requestId)
		{
			if (requestId == 0)
			{
				return;
			}

			lock (_lock)
			{
				_sentRequests.Remove(requestId);
			}
		}

		public void Clear()
		{
			lock (_lock)
			{
				CancellationTokenHelper.CancelAndDispose(ref _cancellation);
				_pendingSends.Clear();
				_sentRequests.Clear();
				_drainStarted = false;
				_sendWindowStartTimestamp = 0;
				_requestsSentInWindow = 0;
			}
		}

		public void Dispose()
		{
			Clear();
		}

		private void Send(PendingSend pendingSend)
		{
			bool sendNow = false;
			bool startDrain = false;

			lock (_lock)
			{
				if (_pendingSends.Count == 0 && TryReserveSendSlot(out _))
				{
					sendNow = true;
				}
				else
				{
					_pendingSends.Enqueue(pendingSend);
					startDrain = true;
				}
			}

			if (startDrain)
			{
				StartDrainQueuedRequests();
			}

			if (sendNow)
			{
				SendNow(pendingSend);
			}
		}

		private void StartDrainQueuedRequests()
		{
			CancellationToken cancellation;

			lock (_lock)
			{
				if (_drainStarted)
				{
					return;
				}

				_drainStarted = true;
				_cancellation ??= new CancellationTokenSource();
				cancellation = _cancellation.Token;
			}

			DrainQueuedRequests(cancellation).Forget();
		}

		private async UniTask DrainQueuedRequests(CancellationToken cancellation)
		{
			try
			{
				while (!cancellation.IsCancellationRequested)
				{
					PendingSend pendingSend = null;
					int delayMs;

					lock (_lock)
					{
						if (_pendingSends.Count == 0)
						{
							return;
						}

						if (TryReserveSendSlot(out delayMs))
						{
							pendingSend = DequeuePendingSend();
						}
					}

					if (pendingSend == null)
					{
						await _delay(delayMs, cancellation);
						continue;
					}

					SendNow(pendingSend);
				}
			}
			catch (OperationCanceledException)
			{
				// This is OK. Just terminating the task
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Drain queued requests failed");
			}
			finally
			{
				bool startDrain = false;

				lock (_lock)
				{
					_drainStarted = false;

					if (!cancellation.IsCancellationRequested && _pendingSends.Count > 0)
					{
						startDrain = true;
					}
				}

				if (startDrain)
				{
					StartDrainQueuedRequests();
				}
			}
		}

		private PendingSend DequeuePendingSend()
		{
			var pendingSend = _pendingSends.Dequeue();

			if (pendingSend.Batch != null || !pendingSend.AutoBatchAllowed || !CanAutoBatch(pendingSend.Message))
			{
				return pendingSend;
			}

			List<IDictionary<string, object>> batch = null;

			while (_pendingSends.Count > 0 && (batch?.Count ?? 1) < SnipeClient.MAX_BATCH_SIZE)
			{
				var nextSend = _pendingSends.Peek();

				if (nextSend.Batch != null || !nextSend.AutoBatchAllowed || !CanAutoBatch(nextSend.Message))
				{
					break;
				}

				_pendingSends.Dequeue();
				batch ??= new List<IDictionary<string, object>>(SnipeClient.MAX_BATCH_SIZE)
				{
					pendingSend.Message,
				};
				batch.Add(nextSend.Message);
			}

			if (batch == null)
			{
				return pendingSend;
			}

			return new PendingSend()
			{
				Batch = batch,
			};
		}

		private void SendNow(PendingSend pendingSend)
		{
			if (pendingSend.Batch != null)
			{
				if (_sendBatch(pendingSend.Batch))
				{
					TrackSentRequests(pendingSend.Batch);
				}
			}
			else if (_sendRequest(pendingSend.Message))
			{
				TrackSentRequest(pendingSend.Message);
			}
		}

		private bool TryReserveSendSlot(out int delayMs)
		{
			delayMs = 0;

			long now = _getTimestamp();

			if (_sendWindowStartTimestamp == 0)
			{
				_sendWindowStartTimestamp = now;
				_requestsSentInWindow = 0;
			}

			double elapsedMs = (now - _sendWindowStartTimestamp) * 1000d / _timestampFrequency;

			if (elapsedMs >= RATE_LIMIT_INTERVAL_MS)
			{
				_sendWindowStartTimestamp = now;
				_requestsSentInWindow = 0;
				elapsedMs = 0;
			}

			if (_requestsSentInWindow < SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT)
			{
				_requestsSentInWindow++;
				return true;
			}

			delayMs = Math.Max(1, RATE_LIMIT_INTERVAL_MS - (int)elapsedMs);
			return false;
		}

		private void TrackSentRequests(List<IDictionary<string, object>> messages)
		{
			for (int i = 0; i < messages.Count; i++)
			{
				TrackSentRequest(messages[i]);
			}
		}

		private void TrackSentRequest(IDictionary<string, object> message)
		{
			if (message == null)
			{
				return;
			}

			int requestId = message.SafeGetValue<int>("id");

			if (requestId == 0)
			{
				return;
			}

			lock (_lock)
			{
				if (_sentRequests.TryGetValue(requestId, out var request))
				{
					request.RetryScheduled = false;
				}
				else
				{
					_sentRequests[requestId] = new SentRequest()
					{
						RequestId = requestId,
						Message = message,
					};
				}
			}
		}

		private async UniTask RetryRateLimitedRequest(SentRequest request, int delayMs, CancellationToken cancellation)
		{
			try
			{
				await _delay(delayMs, cancellation);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (cancellation.IsCancellationRequested || !_connected())
			{
				return;
			}

			lock (_lock)
			{
				if (!_sentRequests.TryGetValue(request.RequestId, out var current) || !object.ReferenceEquals(current, request))
				{
					return;
				}
			}

			Send(request.Message, false);
		}

		private static bool CanAutoBatch(IDictionary<string, object> message)
		{
			if (message == null)
			{
				return false;
			}

			string json = JsonUtility.ToJson(message);
			return json != null && json.Length <= SnipeClient.DEFAULT_AUTO_BATCH_MAX_MESSAGE_BYTES;
		}
	}
}
