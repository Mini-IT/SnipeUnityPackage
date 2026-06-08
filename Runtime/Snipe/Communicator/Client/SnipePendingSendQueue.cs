using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	internal sealed class SnipePendingSendQueue
	{
		private readonly Queue<SnipePendingSend> _pendingSends = new Queue<SnipePendingSend>();
		private int _pendingRequestCount;

		public bool IsEmpty => _pendingSends.Count == 0;
		public int Count => _pendingSends.Count;

		public bool TryEnqueue(SnipePendingSend pendingSend, int maxPendingRequestCount, out int pendingRequestCount, out int queuedSendCount)
		{
			pendingRequestCount = _pendingRequestCount + pendingSend.RequestCount;

			if (pendingRequestCount > maxPendingRequestCount)
			{
				queuedSendCount = _pendingSends.Count;
				return false;
			}

			_pendingSends.Enqueue(pendingSend);
			_pendingRequestCount = pendingRequestCount;
			queuedSendCount = _pendingSends.Count;
			return true;
		}

		public SnipePendingSend Dequeue(int maxRequestCount)
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

			return SnipePendingSend.CreateBatch(batch);
		}

		public void AddDroppedRequests(ref List<IDictionary<string, object>> droppedRequests)
		{
			while (_pendingSends.Count > 0)
			{
				_pendingSends.Dequeue().AddDroppedRequests(ref droppedRequests);
			}

			_pendingRequestCount = 0;
		}

		public void Clear()
		{
			_pendingSends.Clear();
			_pendingRequestCount = 0;
		}

		private void RemovePendingRequests(int requestCount)
		{
			_pendingRequestCount = Math.Max(0, _pendingRequestCount - requestCount);
		}
	}
}
