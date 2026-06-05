using System.Collections.Generic;

namespace MiniIT.Snipe
{
	internal sealed class SnipeRequestBatchBuffer
	{
		private readonly object _lock = new object();
		private Queue<IDictionary<string, object>> _requests;

		public bool Enabled
		{
			get
			{
				lock (_lock)
				{
					return _requests != null;
				}
			}
		}

		public bool IsEmpty
		{
			get
			{
				lock (_lock)
				{
					return _requests == null || _requests.Count == 0;
				}
			}
		}

		public List<IDictionary<string, object>> SetEnabled(bool value)
		{
			lock (_lock)
			{
				if (value == (_requests != null))
				{
					return null;
				}

				if (value)
				{
					_requests ??= new Queue<IDictionary<string, object>>();
					return null;
				}

				// Disable and flush under one lock so a concurrent Add cannot enqueue into an orphaned buffer.
				var messages = FlushLocked();
				_requests = null;
				return messages;
			}
		}

		public List<IDictionary<string, object>> Add(IDictionary<string, object> message)
		{
			if (message == null)
			{
				return null;
			}

			lock (_lock)
			{
				if (_requests == null)
				{
					// BatchMode can change between caller check and Add; send this request unbatched.
					return new List<IDictionary<string, object>>(1)
					{
						message,
					};
				}

				_requests.Enqueue(message);

				if (_requests.Count >= SnipeClient.MAX_BATCH_SIZE)
				{
					return FlushLocked();
				}

				return null;
			}
		}

		public void Clear()
		{
			lock (_lock)
			{
				// Connection reset must not leave old request ids waiting for the next session.
				_requests?.Clear();
			}
		}

		private List<IDictionary<string, object>> FlushLocked()
		{
			if (_requests == null || _requests.Count == 0)
			{
				return null;
			}

			var messages = new List<IDictionary<string, object>>(_requests.Count);

			while (_requests.Count > 0)
			{
				messages.Add(_requests.Dequeue());
			}

			return messages;
		}
	}
}
