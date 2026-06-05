using System.Collections.Generic;

namespace MiniIT.Snipe
{
	internal sealed class SnipeSentRequestTracker
	{
		private const int MAX_SENT_REQUESTS_COUNT = 512;

		internal sealed class TrackedRequest
		{
			public int RequestId;
			public IDictionary<string, object> Message;
			public bool RetryScheduled;
			public bool RateLimited;
			public LinkedListNode<int> Node;
		}

		private readonly Dictionary<int, TrackedRequest> _requests = new Dictionary<int, TrackedRequest>();
		private readonly LinkedList<int> _requestIds = new LinkedList<int>();
		private readonly IAnalyticsContext _analytics;

		private int _retryDelayMs = SnipeClient.MIN_RATE_LIMIT_RETRY_DELAY_MS;
		private int _cooldownId;
		private int _rateLimitedRequestCount;
		private bool _cooldownActive;

		public SnipeSentRequestTracker(IAnalyticsContext analytics)
		{
			_analytics = analytics;
		}

		public void Track(IDictionary<string, object> message)
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

			if (_requests.TryGetValue(requestId, out var request))
			{
				// A retry reuses the same id; refresh the message snapshot for the next 429.
				request.RetryScheduled = false;
				request.Message = message;
				Refresh(request);
			}
			else
			{
				var trackedRequest = new TrackedRequest()
				{
					RequestId = requestId,
					Message = message,
				};

				trackedRequest.Node = _requestIds.AddLast(requestId);
				_requests[requestId] = trackedRequest;
				Trim();
			}
		}

		public void Remove(int requestId)
		{
			if (_requests.TryGetValue(requestId, out var request))
			{
				_requests.Remove(requestId);
				_requestIds.Remove(request.Node);
				OnRemoved(requestId, request, false);
			}
		}

		public bool TryScheduleRateLimitRetry(int requestId, out TrackedRequest request, out int delayMs, out int cooldownId)
		{
			request = null;
			delayMs = 0;
			cooldownId = 0;

			if (!_requests.TryGetValue(requestId, out request))
			{
				return false;
			}

			if (request.RetryScheduled)
			{
				request = null;
				return true;
			}

			request.RetryScheduled = true;
			if (!request.RateLimited)
			{
				request.RateLimited = true;
				_rateLimitedRequestCount++;
			}

			if (!_cooldownActive)
			{
				// All requests hit by one throttling wave share one backoff step.
				_cooldownActive = true;
				_cooldownId++;
			}

			delayMs = _retryDelayMs;
			cooldownId = _cooldownId;
			return true;
		}

		public bool TryClearScheduledRetry(int requestId)
		{
			if (_requests.TryGetValue(requestId, out var request) && request.RetryScheduled)
			{
				request.RetryScheduled = false;
				return request.RateLimited;
			}

			return false;
		}

		public void ReleaseRetryCooldown(int cooldownId)
		{
			if (_cooldownActive && _cooldownId == cooldownId)
			{
				_cooldownActive = false;
				// Increase once per cooldown wave, not once per request in that wave.
				_retryDelayMs = System.Math.Min(_retryDelayMs * 2, SnipeClient.MAX_RATE_LIMIT_RETRY_DELAY_MS);
			}
		}

		public bool IsCurrent(TrackedRequest request)
		{
			return request != null &&
			       _requests.TryGetValue(request.RequestId, out var current) &&
			       object.ReferenceEquals(current, request);
		}

		public void ClearRetryScheduled(TrackedRequest request)
		{
			if (IsCurrent(request))
			{
				request.RetryScheduled = false;
			}
		}

		public void AddDroppedRequests(ref List<IDictionary<string, object>> droppedRequests)
		{
			foreach (var request in _requests.Values)
			{
				AddDroppedRequest(ref droppedRequests, request.Message);
			}
		}

		public void Clear()
		{
			_requests.Clear();
			_requestIds.Clear();
			_rateLimitedRequestCount = 0;
			ResetRetryDelay();
		}

		private void Refresh(TrackedRequest request)
		{
			_requestIds.Remove(request.Node);
			request.Node = _requestIds.AddLast(request.RequestId);
		}

		private void Trim()
		{
			while (_requests.Count > MAX_SENT_REQUESTS_COUNT)
			{
				// Never evict scheduled retries; losing them would surface avoidable rate-limit errors.
				var node = GetEvictableRequestNode();

				if (node == null)
				{
					return;
				}

				int requestId = node.Value;
				_requestIds.Remove(node);

				if (_requests.TryGetValue(requestId, out var request))
				{
					_requests.Remove(requestId);
					OnRemoved(requestId, request, true);
				}
			}
		}

		private LinkedListNode<int> GetEvictableRequestNode()
		{
			var node = _requestIds.First;

			while (node != null)
			{
				var next = node.Next;

				if (!_requests.TryGetValue(node.Value, out var request) || !request.RetryScheduled)
				{
					return node;
				}

				node = next;
			}

			return null;
		}

		private void OnRemoved(int requestId, TrackedRequest request, bool evicted)
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
				ResetRetryDelay();
			}
		}

		private void ResetRetryDelay()
		{
			_retryDelayMs = SnipeClient.MIN_RATE_LIMIT_RETRY_DELAY_MS;
			_cooldownActive = false;
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
	}
}
