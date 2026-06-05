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
		public Action<List<IDictionary<string, object>>> OnPendingQueueOverflow;
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
		private const int JITTER_PERCENT = 25;

		private static readonly object s_jitterLock = new object();
		private static readonly Random s_jitterRandom = new Random();

		private sealed class PendingSend
		{
			public IDictionary<string, object> Message;
			public List<IDictionary<string, object>> Batch;
			public bool AutoBatchAllowed;

			public int RequestCount => Batch?.Count ?? 1;
			public bool IsBatch => Batch != null;
			public bool CanJoinAutoBatch => Batch == null && AutoBatchAllowed && SnipeRequestDispatcher.CanAutoBatch(Message);

			public static PendingSend CreateMessage(IDictionary<string, object> message, bool autoBatchAllowed)
			{
				return new PendingSend()
				{
					Message = message,
					AutoBatchAllowed = autoBatchAllowed,
				};
			}

			public static PendingSend CreateBatch(List<IDictionary<string, object>> batch)
			{
				return new PendingSend()
				{
					Batch = batch,
				};
			}

			public PendingSend SplitBatch(int count)
			{
				var batch = new List<IDictionary<string, object>>(count);
				var remainder = new List<IDictionary<string, object>>(Batch.Count - count);

				for (int i = 0; i < Batch.Count; i++)
				{
					if (i < count)
					{
						batch.Add(Batch[i]);
					}
					else
					{
						remainder.Add(Batch[i]);
					}
				}

				Batch = remainder;

				return CreateBatch(batch);
			}

			public bool TrySend(Func<IDictionary<string, object>, bool> sendRequest, Func<List<IDictionary<string, object>>, bool> sendBatch)
			{
				return IsBatch ? sendBatch(Batch) : sendRequest(Message);
			}

			public void TrackSent(SnipeRequestDispatcher dispatcher)
			{
				if (IsBatch)
				{
					for (int i = 0; i < Batch.Count; i++)
					{
						dispatcher.TrackSentRequest(Batch[i]);
					}
				}
				else
				{
					dispatcher.TrackSentRequest(Message);
				}
			}

			public void HandleSendFailure(SnipeRequestDispatcher dispatcher)
			{
				if (IsBatch)
				{
					for (int i = 0; i < Batch.Count; i++)
					{
						dispatcher.HandleSendFailure(Batch[i]);
					}
				}
				else
				{
					dispatcher.HandleSendFailure(Message);
				}
			}

			public void AddDroppedRequests(ref List<IDictionary<string, object>> droppedRequests)
			{
				if (IsBatch)
				{
					for (int i = 0; i < Batch.Count; i++)
					{
						AddDroppedRequest(ref droppedRequests, Batch[i]);
					}
				}
				else
				{
					AddDroppedRequest(ref droppedRequests, Message);
				}
			}
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
		private readonly List<PendingSend> _reservedSends = new List<PendingSend>();
		private readonly SnipeSentRequestTracker _sentRequestTracker;
		private readonly Func<IDictionary<string, object>, bool> _sendRequest;
		private readonly Func<List<IDictionary<string, object>>, bool> _sendBatch;
		private readonly Func<bool> _connected;
		private readonly Action<List<IDictionary<string, object>>> _onPendingQueueOverflow;
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

		internal SnipeRequestDispatcher(SnipeRequestDispatcherOptions options)
		{
			_sendRequest = options.SendRequest ?? throw new ArgumentNullException(nameof(options.SendRequest));
			_sendBatch = options.SendBatch ?? throw new ArgumentNullException(nameof(options.SendBatch));
			_connected = options.IsConnected ?? throw new ArgumentNullException(nameof(options.IsConnected));
			_onPendingQueueOverflow = options.OnPendingQueueOverflow;
			_analytics = options.Analytics ?? throw new ArgumentNullException(nameof(options.Analytics));
			_sentRequestTracker = new SnipeSentRequestTracker(_analytics);
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

			Send(PendingSend.CreateBatch(messages), sendGeneration);
		}

		public bool TryHandleRateLimit(int requestId)
		{
			SnipeSentRequestTracker.TrackedRequest request;
			int delayMs;
			int cooldownId;
			int sendGeneration;
			CancellationToken cancellation;

			lock (_lock)
			{
				if (!_sentRequestTracker.TryScheduleRateLimitRetry(requestId, out request, out delayMs, out cooldownId))
				{
					return false;
				}

				if (request == null)
				{
					return true;
				}

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
				_sentRequestTracker.Remove(requestId);
			}
		}

		public List<IDictionary<string, object>> DropAll()
		{
			return DropAll(null);
		}

		private List<IDictionary<string, object>> DropAll(PendingSend extraPendingSend)
		{
			lock (_sendGate)
			{
				lock (_lock)
				{
					return DropAllLocked(extraPendingSend);
				}
			}
		}

		public void Dispose()
		{
			DropAll();
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
				int requestCount = pendingSend.RequestCount;
				bool queueWasEmpty = _pendingSends.Count == 0;
				bool limitReached = requestCount > availableRequestSlots;

				if (queueWasEmpty && !_queuedSendInProgress && !limitReached)
				{
					// Reserve before leaving the lock so concurrent callers cannot overshoot the window.
					ReserveSend(pendingSend);
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
				var droppedRequests = DropAll(pendingSend);
				HandlePendingQueueOverflow(pendingRequestCount, droppedRequests);
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

			Send(PendingSend.CreateMessage(message, autoBatchAllowed), sendGeneration);
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
								ReserveSend(pendingSend);
								_queuedSendInProgress = true;
								queuedSendInProgress = true;
							}
						}
					}

					if (pendingSend == null)
					{
						await _delay(AddThrottleJitter(delayMs), cancellation);
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

			if (pendingSend.RequestCount > maxRequestCount)
			{
				if (pendingSend.IsBatch)
				{
					var splitBatch = pendingSend.SplitBatch(maxRequestCount);
					RemovePendingRequests(maxRequestCount);
					return splitBatch;
				}

				return null;
			}

			_pendingSends.Dequeue();
			RemovePendingRequests(pendingSend.RequestCount);

			if (!pendingSend.CanJoinAutoBatch)
			{
				return pendingSend;
			}

			List<IDictionary<string, object>> batch = null;

			int batchLimit = Math.Min(SnipeClient.MAX_BATCH_SIZE, maxRequestCount);

			while (_pendingSends.Count > 0 && (batch?.Count ?? 1) < batchLimit)
			{
				var nextSend = _pendingSends.Peek();

				if (!nextSend.CanJoinAutoBatch)
				{
					break;
				}

				_pendingSends.Dequeue();
				RemovePendingRequests(nextSend.RequestCount);
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

			return PendingSend.CreateBatch(batch);
		}

		private void SendNow(PendingSend pendingSend, int sendGeneration)
		{
			bool sent = false;
			bool staleSend = false;

			try
			{
				lock (_sendGate)
				{
					try
					{
						// Clear waits on this gate, so a request cannot be tracked after its session was reset.
						if (!IsCurrentSendGeneration(sendGeneration))
						{
							staleSend = true;
						}
						else
						{
							sent = pendingSend.TrySend(_sendRequest, _sendBatch);

							if (sent)
							{
								if (IsCurrentSendGeneration(sendGeneration))
								{
									pendingSend.TrackSent(this);
								}
								else
								{
									staleSend = true;
								}
							}
						}
					}
					finally
					{
						if (sent || staleSend)
						{
							ReleaseReservedSend(pendingSend, sendGeneration);
						}
					}
				}
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Send request failed");
			}

			if (!staleSend && !sent && ReleaseFailedSend(pendingSend, sendGeneration))
			{
				pendingSend.HandleSendFailure(this);
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
				retryRateLimitedRequest = _sentRequestTracker.TryClearScheduledRetry(requestId);
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

		private void ReserveSend(PendingSend pendingSend)
		{
			_requestsSentInWindow += Math.Max(1, pendingSend.RequestCount);
			_reservedSends.Add(pendingSend);
		}

		private void ReleaseReservedSend(PendingSend pendingSend, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration == _sendGeneration)
				{
					_reservedSends.Remove(pendingSend);
				}
			}
		}

		private bool ReleaseFailedSend(PendingSend pendingSend, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration != _sendGeneration)
				{
					return false;
				}

				_requestsSentInWindow = Math.Max(0, _requestsSentInWindow - Math.Max(1, pendingSend.RequestCount));
				_reservedSends.Remove(pendingSend);
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

		private void TrackSentRequest(IDictionary<string, object> message)
		{
			lock (_lock)
			{
				_sentRequestTracker.Track(message);
			}
		}

		private void ReleaseRateLimitRetryCooldown(int cooldownId, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration == _sendGeneration)
				{
					_sentRequestTracker.ReleaseRetryCooldown(cooldownId);
				}
			}
		}

		private async UniTask RetryRateLimitedRequest(SnipeSentRequestTracker.TrackedRequest request, int delayMs, int cooldownId, int sendGeneration, CancellationToken cancellation)
		{
			try
			{
				await _delay(AddRateLimiterJitter(delayMs), cancellation);
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
				if (sendGeneration != _sendGeneration || !_sentRequestTracker.IsCurrent(request))
				{
					// Response, eviction, or reconnect already replaced this retry target.
					return;
				}
			}

			Send(request.Message, false, sendGeneration);
		}

		private void ClearRetryScheduled(SnipeSentRequestTracker.TrackedRequest request, int sendGeneration)
		{
			lock (_lock)
			{
				if (sendGeneration == _sendGeneration)
				{
					_sentRequestTracker.ClearRetryScheduled(request);
				}
			}
		}

		private static bool CanAutoBatch(IDictionary<string, object> message)
		{
			return message != null && SnipeRequestMessageSizeEsimator.EstimateSizeSmall(message);
		}

		private int AddThrottleJitter(int delayMs)
		{
			return Math.Max(1, delayMs + _getJitterDelayMs(delayMs));
		}

		private int AddRateLimiterJitter(int delayMs)
		{
			return Math.Max(SnipeClient.MIN_RATE_LIMIT_RETRY_DELAY_MS, delayMs + _getJitterDelayMs(delayMs));
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

				Send(PendingSend.CreateBatch(chunk), sendGeneration);
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

		private List<IDictionary<string, object>> DropAllLocked(PendingSend extraPendingSend)
		{
			var droppedRequests = CollectDroppedRequests(extraPendingSend);

			_sendGeneration++;
			CancellationTokenHelper.CancelAndDispose(ref _cancellation);
			_pendingRequestCount = 0;
			_sentRequestTracker.Clear();
			_reservedSends.Clear();
			_drainStarted = false;
			_queuedSendInProgress = false;
			_sendWindowStartTimestamp = 0;
			_requestsSentInWindow = 0;

			return droppedRequests;
		}

		private List<IDictionary<string, object>> CollectDroppedRequests(PendingSend extraPendingSend)
		{
			List<IDictionary<string, object>> droppedRequests = null;

			AddDroppedRequests(ref droppedRequests, extraPendingSend);

			for (int i = 0; i < _reservedSends.Count; i++)
			{
				AddDroppedRequests(ref droppedRequests, _reservedSends[i]);
			}

			_sentRequestTracker.AddDroppedRequests(ref droppedRequests);

			while (_pendingSends.Count > 0)
			{
				AddDroppedRequests(ref droppedRequests, _pendingSends.Dequeue());
			}

			return droppedRequests;
		}

		private static void AddDroppedRequests(ref List<IDictionary<string, object>> droppedRequests, PendingSend pendingSend)
		{
			if (pendingSend == null)
			{
				return;
			}

			pendingSend.AddDroppedRequests(ref droppedRequests);
		}

		private static void AddDroppedRequest(ref List<IDictionary<string, object>> droppedRequests, IDictionary<string, object> message)
		{
			if (message == null)
			{
				return;
			}

			int requestId = message.SafeGetValue<int>("id");
			if (requestId != 0 && droppedRequests != null)
			{
				for (int i = 0; i < droppedRequests.Count; i++)
				{
					if (droppedRequests[i].SafeGetValue<int>("id") == requestId)
					{
						return;
					}
				}
			}

			droppedRequests ??= new List<IDictionary<string, object>>();
			droppedRequests.Add(message);
		}

		private void HandlePendingQueueOverflow(int pendingRequestCount, List<IDictionary<string, object>> droppedRequests)
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
				_onPendingQueueOverflow?.Invoke(droppedRequests);
			}
		}
	}
}
