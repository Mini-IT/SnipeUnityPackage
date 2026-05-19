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
	internal struct SnipeRequestDispatcherOptions
	{
		public Func<IDictionary<string, object>, bool> SendRequest;
		public Func<List<IDictionary<string, object>>, bool> SendBatch;
		public Func<bool> Connected;
		public Func<long> GetTimestamp;
		public Func<int, CancellationToken, UniTask> Delay;
		public Func<int> GetRequestsPerSecondLimit;
		public Func<int, int> GetJitterDelayMs;
		public long TimestampFrequency;
		public IAnalyticsContext Analytics;
		public ILogger Logger;
	}

	internal sealed class SnipeRequestDispatcher : IDisposable
	{
		private const int RATE_LIMIT_INTERVAL_MS = 1000;
		private const int MAX_SENT_REQUESTS_COUNT = 512;
		private const int JITTER_PERCENT = 25;

		private static readonly object s_jitterLock = new object();
		private static readonly Random s_jitterRandom = new Random();

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
			public bool RetryScheduled;
			public bool RateLimited;
			public LinkedListNode<int> Node;
		}

		private sealed class EmptyLogger : ILogger
		{
			public static EmptyLogger Instance => s_instance ??= new EmptyLogger();
			private static EmptyLogger s_instance;

			public IDisposable BeginScope<TState>(TState state) => null;
			public bool IsEnabled(LogLevel logLevel) => false;
			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
		}

		private readonly object _lock = new object();
		private readonly Queue<PendingSend> _pendingSends = new Queue<PendingSend>();
		private readonly Dictionary<int, SentRequest> _sentRequests = new Dictionary<int, SentRequest>();
		private readonly LinkedList<int> _sentRequestIds = new LinkedList<int>();
		private readonly Func<IDictionary<string, object>, bool> _sendRequest;
		private readonly Func<List<IDictionary<string, object>>, bool> _sendBatch;
		private readonly Func<bool> _connected;
		private readonly Func<long> _getTimestamp;
		private readonly Func<int, CancellationToken, UniTask> _delay;
		private readonly Func<int> _getRequestsPerSecondLimit;
		private readonly Func<int, int> _getJitterDelayMs;
		private readonly long _timestampFrequency;
		private readonly IAnalyticsContext _analytics;
		private readonly ILogger _logger;

		private CancellationTokenSource _cancellation;
		private bool _drainStarted;
		private long _sendWindowStartTimestamp;
		private int _requestsSentInWindow;
		private int _rateLimitRetryDelayMs = SnipeClient.RATE_LIMIT_RETRY_DELAY_MS;
		private int _rateLimitRetryCooldownId;
		private int _rateLimitedRequestCount;
		private bool _rateLimitRetryCooldownActive;

		internal SnipeRequestDispatcher(SnipeRequestDispatcherOptions options)
		{
			_sendRequest = options.SendRequest ?? throw new ArgumentNullException(nameof(options.SendRequest));
			_sendBatch = options.SendBatch ?? throw new ArgumentNullException(nameof(options.SendBatch));
			_connected = options.Connected ?? throw new ArgumentNullException(nameof(options.Connected));
			_analytics = options.Analytics ?? throw new ArgumentNullException(nameof(options.Analytics));
			_logger = options.Logger ?? EmptyLogger.Instance;
			_getTimestamp = options.GetTimestamp ?? Stopwatch.GetTimestamp;
			_timestampFrequency = options.TimestampFrequency > 0 ? options.TimestampFrequency : Stopwatch.Frequency;
			_delay = options.Delay ?? DefaultDelay;
			_getRequestsPerSecondLimit = options.GetRequestsPerSecondLimit ?? GetDefaultRequestsPerSecondLimit;
			_getJitterDelayMs = options.GetJitterDelayMs ?? GetRandomJitterDelayMs;
		}

		private static UniTask DefaultDelay(int delayMs, CancellationToken cancellation)
		{
			return AlterTask.Delay(delayMs, cancellation).AsUniTask();
		}

		private static int GetDefaultRequestsPerSecondLimit()
		{
			return SnipeOptions.DEFAULT_REQUESTS_PER_SECOND_LIMIT;
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

			int maxBatchSize = Math.Min(SnipeClient.MAX_BATCH_SIZE, GetRequestsPerSecondLimit());

			if (messages.Count > maxBatchSize)
			{
				SendBatchChunks(messages, maxBatchSize);
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
			int cooldownId;
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
				if (!request.RateLimited)
				{
					request.RateLimited = true;
					_rateLimitedRequestCount++;
				}

				if (!_rateLimitRetryCooldownActive)
				{
					_rateLimitRetryCooldownActive = true;
					_rateLimitRetryCooldownId++;
				}

				delayMs = _rateLimitRetryDelayMs;
				cooldownId = _rateLimitRetryCooldownId;
				_cancellation ??= new CancellationTokenSource();
				cancellation = _cancellation.Token;
			}

			RetryRateLimitedRequest(request, delayMs, cooldownId, cancellation).Forget();
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
				if (_sentRequests.TryGetValue(requestId, out var request))
				{
					_sentRequests.Remove(requestId);
					_sentRequestIds.Remove(request.Node);
					OnSentRequestRemoved(requestId, request, false);
				}
			}
		}

		public void Clear()
		{
			lock (_lock)
			{
				CancellationTokenHelper.CancelAndDispose(ref _cancellation);
				_pendingSends.Clear();
				_sentRequests.Clear();
				_sentRequestIds.Clear();
				_drainStarted = false;
				_sendWindowStartTimestamp = 0;
				_requestsSentInWindow = 0;
				_rateLimitedRequestCount = 0;
				ResetRateLimitRetryDelay();
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
			bool logRateLimitReached = false;
			bool logQueueingStarted = false;
			int queuedSendCount = 0;
			int requestsPerSecondLimit = GetRequestsPerSecondLimit();

			lock (_lock)
			{
				int availableRequestSlots = GetAvailableRequestSlots(requestsPerSecondLimit, out _);
				int requestCount = GetRequestCount(pendingSend);

				if (_pendingSends.Count == 0 && requestCount <= availableRequestSlots)
				{
					ReserveRequestSlots(requestCount);
					sendNow = true;
				}
				else
				{
					logRateLimitReached = requestCount > availableRequestSlots;
					logQueueingStarted = _pendingSends.Count == 0;
					_pendingSends.Enqueue(pendingSend);
					queuedSendCount = _pendingSends.Count;
					startDrain = true;
				}
			}

			if (logQueueingStarted)
			{
				_logger.LogDebug("Started queueing requests. Limit reached: {0}", requestsPerSecondLimit);
				_analytics.TrackEvent("Requests rate limit reached", "requestsPerSecondLimit", requestsPerSecondLimit);
			}

			if (logRateLimitReached)
			{
				_logger.LogDebug("Requests queued: {0}", queuedSendCount);
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

						int requestsPerSecondLimit = GetRequestsPerSecondLimit();
						int availableRequestSlots = GetAvailableRequestSlots(requestsPerSecondLimit, out delayMs);

						if (availableRequestSlots > 0)
						{
							pendingSend = DequeuePendingSend(availableRequestSlots);

							if (pendingSend != null)
							{
								ReserveRequestSlots(GetRequestCount(pendingSend));
							}
						}
					}

					if (pendingSend == null)
					{
						await _delay(AddJitter(delayMs), cancellation);
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
				bool logQueueingEnded = false;

				lock (_lock)
				{
					_drainStarted = false;

					if (!cancellation.IsCancellationRequested && _pendingSends.Count > 0)
					{
						startDrain = true;
					}
					else if (!cancellation.IsCancellationRequested)
					{
						logQueueingEnded = true;
					}
				}

				if (logQueueingEnded)
				{
					_logger.LogDebug("Ended queueing requests");
				}

				if (startDrain)
				{
					StartDrainQueuedRequests();
				}
			}
		}

		private PendingSend DequeuePendingSend(int maxRequestCount)
		{
			var pendingSend = _pendingSends.Peek();

			if (GetRequestCount(pendingSend) > maxRequestCount)
			{
				if (pendingSend.Batch != null)
				{
					return SplitPendingBatch(pendingSend, maxRequestCount);
				}

				return null;
			}

			_pendingSends.Dequeue();

			if (pendingSend.Batch != null || !pendingSend.AutoBatchAllowed || !CanAutoBatch(pendingSend.Message))
			{
				return pendingSend;
			}

			List<IDictionary<string, object>> batch = null;

			int batchLimit = Math.Min(SnipeClient.MAX_BATCH_SIZE, maxRequestCount);

			while (_pendingSends.Count > 0 && (batch?.Count ?? 1) < batchLimit)
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

		private static PendingSend SplitPendingBatch(PendingSend pendingSend, int count)
		{
			var batch = new List<IDictionary<string, object>>(count);
			var remainder = new List<IDictionary<string, object>>(pendingSend.Batch.Count - count);

			for (int i = 0; i < pendingSend.Batch.Count; i++)
			{
				if (i < count)
				{
					batch.Add(pendingSend.Batch[i]);
				}
				else
				{
					remainder.Add(pendingSend.Batch[i]);
				}
			}

			pendingSend.Batch = remainder;

			return new PendingSend()
			{
				Batch = batch,
			};
		}

		private void SendNow(PendingSend pendingSend)
		{
			bool sent = false;

			try
			{
				if (pendingSend.Batch != null)
				{
					sent = _sendBatch(pendingSend.Batch);

					if (sent)
					{
						TrackSentRequests(pendingSend.Batch);
					}
				}
				else
				{
					sent = _sendRequest(pendingSend.Message);

					if (sent)
					{
						TrackSentRequest(pendingSend.Message);
					}
				}
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Send request failed");
			}

			if (!sent)
			{
				ReleaseRequestSlots(GetRequestCount(pendingSend));
			}
		}

		private int GetAvailableRequestSlots(int requestsPerSecondLimit, out int delayMs)
		{
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

			delayMs = Math.Max(1, RATE_LIMIT_INTERVAL_MS - (int)elapsedMs);
			return Math.Max(0, requestsPerSecondLimit - _requestsSentInWindow);
		}

		private void ReserveRequestSlots(int requestCount)
		{
			_requestsSentInWindow += Math.Max(1, requestCount);
		}

		private void ReleaseRequestSlots(int requestCount)
		{
			lock (_lock)
			{
				_requestsSentInWindow = Math.Max(0, _requestsSentInWindow - Math.Max(1, requestCount));
			}
		}

		private int GetRequestsPerSecondLimit()
		{
			int requestsPerSecondLimit = _getRequestsPerSecondLimit();
			return requestsPerSecondLimit > 0 ? requestsPerSecondLimit : SnipeOptions.DEFAULT_REQUESTS_PER_SECOND_LIMIT;
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
					request.Message = message;
					RefreshSentRequest(request);
				}
				else
				{
					var sentRequest = new SentRequest()
					{
						RequestId = requestId,
						Message = message,
					};

					sentRequest.Node = _sentRequestIds.AddLast(requestId);
					_sentRequests[requestId] = sentRequest;
					TrimSentRequests();
				}
			}
		}

		private void RefreshSentRequest(SentRequest request)
		{
			_sentRequestIds.Remove(request.Node);
			request.Node = _sentRequestIds.AddLast(request.RequestId);
		}

		private void TrimSentRequests()
		{
			while (_sentRequests.Count > MAX_SENT_REQUESTS_COUNT)
			{
				int requestId = _sentRequestIds.First.Value;
				_sentRequestIds.RemoveFirst();

				if (_sentRequests.TryGetValue(requestId, out var request))
				{
					_sentRequests.Remove(requestId);
					OnSentRequestRemoved(requestId, request, true);
				}
			}
		}

		private void OnSentRequestRemoved(int requestId, SentRequest request, bool evicted)
		{
			if (request.RateLimited && _rateLimitedRequestCount > 0)
			{
				_rateLimitedRequestCount--;
			}

			if (evicted)
			{
				_analytics.TrackEvent("Sent request tracking evicted", new Dictionary<string, object>()
				{
					["request_id"] = requestId,
					["rate_limited"] = request.RateLimited,
					["retry_scheduled"] = request.RetryScheduled,
				});
			}

			if (_rateLimitedRequestCount == 0)
			{
				ResetRateLimitRetryDelay();
			}
		}

		private void ResetRateLimitRetryDelay()
		{
			_rateLimitRetryDelayMs = SnipeClient.RATE_LIMIT_RETRY_DELAY_MS;
			_rateLimitRetryCooldownActive = false;
		}

		private void ReleaseRateLimitRetryCooldown(int cooldownId)
		{
			lock (_lock)
			{
				if (_rateLimitRetryCooldownActive && _rateLimitRetryCooldownId == cooldownId)
				{
					_rateLimitRetryCooldownActive = false;
					_rateLimitRetryDelayMs = Math.Min(_rateLimitRetryDelayMs * 2, SnipeClient.MAX_RATE_LIMIT_RETRY_DELAY_MS);
				}
			}
		}

		private async UniTask RetryRateLimitedRequest(SentRequest request, int delayMs, int cooldownId, CancellationToken cancellation)
		{
			try
			{
				await _delay(AddJitter(delayMs), cancellation);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			ReleaseRateLimitRetryCooldown(cooldownId);

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
			return message != null && SnipeRequestMessageSizeEsimator.EstimateSizeSmall(message);
		}

		private int AddJitter(int delayMs)
		{
			return Math.Max(1, delayMs + _getJitterDelayMs(delayMs));
		}

		private static int NoJitterDelay(int delayMs)
		{
			return 0;
		}

		private static int GetRandomJitterDelayMs(int delayMs)
		{
			if (delayMs <= 0)
			{
				return 0;
			}

			int maxJitterMs = Math.Max(1, delayMs * JITTER_PERCENT / 100);

			lock (s_jitterLock)
			{
				return s_jitterRandom.Next(-maxJitterMs, maxJitterMs + 1);
			}
		}

		private static int GetRequestCount(PendingSend pendingSend)
		{
			return pendingSend.Batch?.Count ?? 1;
		}

		private void SendBatchChunks(List<IDictionary<string, object>> messages, int maxBatchSize)
		{
			for (int index = 0; index < messages.Count;)
			{
				int count = Math.Min(maxBatchSize, messages.Count - index);
				var chunk = new List<IDictionary<string, object>>(count);

				for (int i = 0; i < count; i++)
				{
					chunk.Add(messages[index++]);
				}

				SendBatch(chunk);
			}
		}
	}
}
