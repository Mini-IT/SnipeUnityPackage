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
		public Func<bool> IsConnected;
		public Action OnPendingQueueOverflow;
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
		// Clear and SendNow share this gate; generation handles reentrant clears.
		private readonly object _sendGate = new object();
		private readonly Queue<PendingSend> _pendingSends = new Queue<PendingSend>();
		private readonly Dictionary<int, SentRequest> _sentRequests = new Dictionary<int, SentRequest>();
		private readonly LinkedList<int> _sentRequestIds = new LinkedList<int>();
		private readonly Func<IDictionary<string, object>, bool> _sendRequest;
		private readonly Func<List<IDictionary<string, object>>, bool> _sendBatch;
		private readonly Func<bool> _connected;
		private readonly Action _onPendingQueueOverflow;
		private readonly Func<long> _getTimestamp;
		private readonly Func<int, CancellationToken, UniTask> _delay;
		private readonly Func<int> _getRequestsPerSecondLimit;
		private readonly Func<int, int> _getJitterDelayMs;
		private readonly long _timestampFrequency;
		private readonly IAnalyticsContext _analytics;
		private readonly ILogger _logger;

		private CancellationTokenSource _cancellation;
		private int _pendingRequestCount;
		private bool _drainStarted;
		// Keeps newer requests behind a queued item already dequeued by the drain task.
		private bool _queuedSendInProgress;
		// Invalidates delayed drain/retry continuations after Clear().
		private int _sendGeneration;
		private long _sendWindowStartTimestamp;
		private int _requestsSentInWindow;
		private int _rateLimitRetryDelayMs = SnipeClient.MIN_RATE_LIMIT_RETRY_DELAY_MS;
		private int _rateLimitRetryCooldownId;
		private int _rateLimitedRequestCount;
		private bool _rateLimitRetryCooldownActive;

		internal SnipeRequestDispatcher(SnipeRequestDispatcherOptions options)
		{
			_sendRequest = options.SendRequest ?? throw new ArgumentNullException(nameof(options.SendRequest));
			_sendBatch = options.SendBatch ?? throw new ArgumentNullException(nameof(options.SendBatch));
			_connected = options.IsConnected ?? throw new ArgumentNullException(nameof(options.IsConnected));
			_onPendingQueueOverflow = options.OnPendingQueueOverflow;
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
			Send(message, autoBatchAllowed, GetSendGeneration());
		}

		public void SendBatch(List<IDictionary<string, object>> messages)
		{
			if (messages == null || messages.Count == 0)
			{
				return;
			}

			int sendGeneration = GetSendGeneration();
			int maxBatchSize = Math.Min(SnipeClient.MAX_BATCH_SIZE, GetRequestsPerSecondLimit());

			if (messages.Count > maxBatchSize)
			{
				SendBatchChunks(messages, maxBatchSize, sendGeneration);
				return;
			}

			var pendingSend = new PendingSend()
			{
				Batch = messages,
			};

			Send(pendingSend, sendGeneration);
		}

		public bool TryHandleRateLimit(int requestId)
		{
			SentRequest request;
			int delayMs;
			int cooldownId;
			int sendGeneration;
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
					// All requests hit by one throttling wave share one backoff step.
					_rateLimitRetryCooldownActive = true;
					_rateLimitRetryCooldownId++;
				}

				delayMs = _rateLimitRetryDelayMs;
				cooldownId = _rateLimitRetryCooldownId;
				sendGeneration = _sendGeneration;
				_cancellation ??= new CancellationTokenSource();
				cancellation = _cancellation.Token;
			}

			RetryRateLimitedRequest(request, delayMs, cooldownId, sendGeneration, cancellation).Forget();
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
			lock (_sendGate)
			{
				lock (_lock)
				{
					ClearLocked();
				}
			}
		}

		public void Dispose()
		{
			Clear();
		}

		private void Send(PendingSend pendingSend, int sendGeneration)
		{
			bool sendNow = false;
			bool startDrain = false;
			bool logRateLimitReached = false;
			bool logQueueingStarted = false;
			bool pendingQueueOverflow = false;
			int queuedSendCount = 0;
			int pendingRequestCount = 0;
			int requestsPerSecondLimit = GetRequestsPerSecondLimit();

			lock (_lock)
			{
				if (sendGeneration != _sendGeneration)
				{
					return;
				}

				int availableRequestSlots = GetAvailableRequestSlots(requestsPerSecondLimit, out _);
				int requestCount = GetRequestCount(pendingSend);
				bool queueWasEmpty = _pendingSends.Count == 0;
				bool limitReached = requestCount > availableRequestSlots;

				if (queueWasEmpty && !_queuedSendInProgress && !limitReached)
				{
					// Reserve before leaving the lock so concurrent callers cannot overshoot the window.
					ReserveRequestSlots(requestCount);
					sendNow = true;
				}
				else
				{
					logRateLimitReached = limitReached;
					logQueueingStarted = queueWasEmpty && limitReached;
					pendingRequestCount = _pendingRequestCount + requestCount;

					if (pendingRequestCount > SnipeClient.MAX_PENDING_REQUESTS_COUNT)
					{
						pendingQueueOverflow = true;
						ClearLocked();
					}
					else
					{
						EnqueuePendingSend(pendingSend, requestCount);
						queuedSendCount = _pendingSends.Count;
						startDrain = true;
					}
				}
			}

			if (pendingQueueOverflow)
			{
				HandlePendingQueueOverflow(pendingRequestCount);
				return;
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
				SendNow(pendingSend, sendGeneration);
			}
		}

		private void Send(IDictionary<string, object> message, bool autoBatchAllowed, int sendGeneration)
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

			Send(pendingSend, sendGeneration);
		}

		private void StartDrainQueuedRequests()
		{
			CancellationToken cancellation;
			int sendGeneration;

			lock (_lock)
			{
				if (_drainStarted)
				{
					return;
				}

				_drainStarted = true;
				_cancellation ??= new CancellationTokenSource();
				cancellation = _cancellation.Token;
				sendGeneration = _sendGeneration;
			}

			DrainQueuedRequests(sendGeneration, cancellation).Forget();
		}

		private async UniTask DrainQueuedRequests(int sendGeneration, CancellationToken cancellation)
		{
			try
			{
				while (!cancellation.IsCancellationRequested)
				{
					PendingSend pendingSend = null;
					int delayMs;
					bool queuedSendInProgress = false;

					lock (_lock)
					{
						if (sendGeneration != _sendGeneration)
						{
							return;
						}

						if (_pendingSends.Count == 0)
						{
							return;
						}

						int requestsPerSecondLimit = GetRequestsPerSecondLimit();
						int availableRequestSlots = GetAvailableRequestSlots(requestsPerSecondLimit, out delayMs);

						if (availableRequestSlots > 0)
						{
							// Dequeue may split a batch or auto-batch small requests, but preserves queue order.
							pendingSend = DequeuePendingSend(availableRequestSlots);

							if (pendingSend != null)
							{
								ReserveRequestSlots(GetRequestCount(pendingSend));
								_queuedSendInProgress = true;
								queuedSendInProgress = true;
							}
						}
					}

					if (pendingSend == null)
					{
						await _delay(AddJitter(delayMs), cancellation);
						continue;
					}

					try
					{
						SendNow(pendingSend, sendGeneration);
					}
					finally
					{
						if (queuedSendInProgress)
						{
							FinishQueuedSend(sendGeneration);
						}
					}
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
					if (sendGeneration == _sendGeneration)
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

		private void FinishQueuedSend(int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration == _sendGeneration)
				{
					_queuedSendInProgress = false;
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
			RemovePendingRequests(GetRequestCount(pendingSend));

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
				RemovePendingRequests(GetRequestCount(nextSend));
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

		private PendingSend SplitPendingBatch(PendingSend pendingSend, int count)
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
			RemovePendingRequests(count);

			return new PendingSend()
			{
				Batch = batch,
			};
		}

		private void SendNow(PendingSend pendingSend, int sendGeneration)
		{
			bool sent = false;
			bool staleSend = false;

			try
			{
				lock (_sendGate)
				{
					// Clear waits on this gate, so a request cannot be tracked after its session was reset.
					if (!IsCurrentSendGeneration(sendGeneration))
					{
						staleSend = true;
					}
					else if (pendingSend.Batch != null)
					{
						sent = _sendBatch(pendingSend.Batch);

						if (sent)
						{
							if (IsCurrentSendGeneration(sendGeneration))
							{
								TrackSentRequests(pendingSend.Batch);
							}
							else
							{
								staleSend = true;
							}
						}
					}
					else
					{
						sent = _sendRequest(pendingSend.Message);

						if (sent)
						{
							if (IsCurrentSendGeneration(sendGeneration))
							{
								TrackSentRequest(pendingSend.Message);
							}
							else
							{
								staleSend = true;
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Send request failed");
			}

			if (!staleSend && !sent && ReleaseRequestSlots(GetRequestCount(pendingSend), sendGeneration))
			{
				HandleSendFailure(pendingSend);
			}
		}

		private void HandleSendFailure(PendingSend pendingSend)
		{
			if (pendingSend.Batch != null)
			{
				for (int i = 0; i < pendingSend.Batch.Count; i++)
				{
					HandleSendFailure(pendingSend.Batch[i]);
				}
			}
			else
			{
				HandleSendFailure(pendingSend.Message);
			}
		}

		private void HandleSendFailure(IDictionary<string, object> message)
		{
			if (message == null)
			{
				return;
			}

			int requestId = message.SafeGetValue<int>("id");
			bool retryRateLimitedRequest = false;

			lock (_lock)
			{
				if (_sentRequests.TryGetValue(requestId, out var request) && request.RetryScheduled)
				{
					request.RetryScheduled = false;
					retryRateLimitedRequest = request.RateLimited;
				}
			}

			if (retryRateLimitedRequest && _connected())
			{
				TryHandleRateLimit(requestId);
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

		private bool ReleaseRequestSlots(int requestCount, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration != _sendGeneration)
				{
					return false;
				}

				_requestsSentInWindow = Math.Max(0, _requestsSentInWindow - Math.Max(1, requestCount));
				return true;
			}
		}

		private int GetRequestsPerSecondLimit()
		{
			int requestsPerSecondLimit = _getRequestsPerSecondLimit();
			return requestsPerSecondLimit > 0 ? requestsPerSecondLimit : SnipeOptions.DEFAULT_REQUESTS_PER_SECOND_LIMIT;
		}

		private int GetSendGeneration()
		{
			lock (_lock)
			{
				return _sendGeneration;
			}
		}

		private bool IsCurrentSendGeneration(int sendGeneration)
		{
			lock (_lock)
			{
				return sendGeneration == _sendGeneration;
			}
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
					// A retry reuses the same id; refresh the message snapshot for the next 429.
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
				// Never evict scheduled retries; losing them would surface avoidable rate-limit errors.
				var node = GetEvictableSentRequestNode();

				if (node == null)
				{
					return;
				}

				int requestId = node.Value;
				_sentRequestIds.Remove(node);

				if (_sentRequests.TryGetValue(requestId, out var request))
				{
					_sentRequests.Remove(requestId);
					OnSentRequestRemoved(requestId, request, true);
				}
			}
		}

		private LinkedListNode<int> GetEvictableSentRequestNode()
		{
			var node = _sentRequestIds.First;

			while (node != null)
			{
				var next = node.Next;

				if (!_sentRequests.TryGetValue(node.Value, out var request) || !request.RetryScheduled)
				{
					return node;
				}

				node = next;
			}

			return null;
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
			_rateLimitRetryDelayMs = SnipeClient.MIN_RATE_LIMIT_RETRY_DELAY_MS;
			_rateLimitRetryCooldownActive = false;
		}

		private void ReleaseRateLimitRetryCooldown(int cooldownId, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration == _sendGeneration && _rateLimitRetryCooldownActive && _rateLimitRetryCooldownId == cooldownId)
				{
					_rateLimitRetryCooldownActive = false;
					// Increase once per cooldown wave, not once per request in that wave.
					_rateLimitRetryDelayMs = Math.Min(_rateLimitRetryDelayMs * 2, SnipeClient.MAX_RATE_LIMIT_RETRY_DELAY_MS);
				}
			}
		}

		private async UniTask RetryRateLimitedRequest(SentRequest request, int delayMs, int cooldownId, int sendGeneration, CancellationToken cancellation)
		{
			try
			{
				await _delay(AddJitter(delayMs), cancellation);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			ReleaseRateLimitRetryCooldown(cooldownId, sendGeneration);

			if (cancellation.IsCancellationRequested || !_connected())
			{
				ClearRetryScheduled(request, sendGeneration);
				return;
			}

			lock (_lock)
			{
				if (sendGeneration != _sendGeneration ||
				    !_sentRequests.TryGetValue(request.RequestId, out var current) ||
				    !object.ReferenceEquals(current, request))
				{
					// Response, eviction, or reconnect already replaced this retry target.
					return;
				}
			}

			Send(request.Message, false, sendGeneration);
		}

		private void ClearRetryScheduled(SentRequest request, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration == _sendGeneration &&
				    _sentRequests.TryGetValue(request.RequestId, out var current) &&
				    object.ReferenceEquals(current, request))
				{
					request.RetryScheduled = false;
				}
			}
		}

		private static bool CanAutoBatch(IDictionary<string, object> message)
		{
			return message != null && SnipeRequestMessageSizeEsimator.EstimateSizeSmall(message);
		}

		private int AddJitter(int delayMs)
		{
			return Math.Max(SnipeClient.MIN_RATE_LIMIT_RETRY_DELAY_MS, delayMs + _getJitterDelayMs(delayMs));
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

		private void SendBatchChunks(List<IDictionary<string, object>> messages, int maxBatchSize, int sendGeneration)
		{
			for (int index = 0; index < messages.Count;)
			{
				int count = Math.Min(maxBatchSize, messages.Count - index);
				var chunk = new List<IDictionary<string, object>>(count);

				for (int i = 0; i < count; i++)
				{
					chunk.Add(messages[index++]);
				}

				var pendingSend = new PendingSend()
				{
					Batch = chunk,
				};

				Send(pendingSend, sendGeneration);
			}
		}

		private void EnqueuePendingSend(PendingSend pendingSend, int requestCount)
		{
			_pendingSends.Enqueue(pendingSend);
			_pendingRequestCount += requestCount;
		}

		private void RemovePendingRequests(int requestCount)
		{
			_pendingRequestCount = Math.Max(0, _pendingRequestCount - requestCount);
		}

		private void ClearLocked()
		{
			_sendGeneration++;
			CancellationTokenHelper.CancelAndDispose(ref _cancellation);
			_pendingSends.Clear();
			_pendingRequestCount = 0;
			_sentRequests.Clear();
			_sentRequestIds.Clear();
			_drainStarted = false;
			_queuedSendInProgress = false;
			_sendWindowStartTimestamp = 0;
			_requestsSentInWindow = 0;
			_rateLimitedRequestCount = 0;
			ResetRateLimitRetryDelay();
		}

		private void HandlePendingQueueOverflow(int pendingRequestCount)
		{
			try
			{
				_logger.LogError("Pending request limit exceeded: {0}/{1}. Disconnecting.", pendingRequestCount, SnipeClient.MAX_PENDING_REQUESTS_COUNT);
				_analytics.TrackEvent("Pending request limit exceeded", new Dictionary<string, object>()
				{
					["pending_request_count"] = pendingRequestCount,
					["max_pending_request_count"] = SnipeClient.MAX_PENDING_REQUESTS_COUNT,
				});
			}
			finally
			{
				_onPendingQueueOverflow?.Invoke();
			}
		}
	}
}
