using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	internal sealed class SnipeRequestBatchBuffer
	{
		private readonly object _lock = new object();
		private ConcurrentQueue<IDictionary<string, object>> _requests;

		public bool Enabled => _requests != null;
		public bool IsEmpty => _requests == null || _requests.IsEmpty;

		public List<IDictionary<string, object>> SetEnabled(bool value)
		{
			if (value == Enabled)
			{
				return null;
			}

			if (value)
			{
				_requests ??= new ConcurrentQueue<IDictionary<string, object>>();
				return null;
			}

			var messages = Flush();
			_requests = null;
			return messages;
		}

		public List<IDictionary<string, object>> Add(IDictionary<string, object> message)
		{
			if (_requests == null || message == null)
			{
				return null;
			}

			_requests.Enqueue(message);

			if (_requests.Count >= SnipeClient.MAX_BATCH_SIZE)
			{
				return Flush();
			}

			return null;
		}

		public void Clear()
		{
			lock (_lock)
			{
				_requests?.Clear();
			}
		}

		private List<IDictionary<string, object>> Flush()
		{
			IDictionary<string, object>[] queue;

			lock (_lock)
			{
				if (_requests == null || _requests.IsEmpty)
				{
					return null;
				}

				// local copy for thread safety
				queue = _requests.ToArray();
				_requests.Clear();
			}

			var messages = new List<IDictionary<string, object>>(queue.Length);

			for (int i = 0; i < queue.Length; i++)
			{
				messages.Add(queue[i]);
			}

			return messages;
		}
	}
}
