using System;
using System.Collections.Generic;
using System.Threading;

namespace MiniIT.Snipe
{
	public class SnipeContextReference : ISnipeContextReference
	{
		private class CancellableCallback
		{
			internal Action<SnipeContext> callback;
			internal CancellationToken cancellationToken;
		}

		private SnipeContext _context;
		private List<CancellableCallback> _callbacks;
		private readonly object _lock = new object();

		public bool TryGetSnipeContext(out SnipeContext context)
		{
			if (_context != null)
			{
				context = _context;
				return true;
			}

			context = null;
			return false;
		}

		public void GetSnipeContextWhenReady(Action<SnipeContext> callback, CancellationToken cancellationToken = default)
		{
			if (_context != null)
			{
				callback.Invoke(_context);
				return;
			}

			lock (_lock)
			{
				_callbacks ??= new List<CancellableCallback>();
				_callbacks.Add(new CancellableCallback { callback = callback, cancellationToken = cancellationToken });
			}
		}

		public void SetContext(SnipeContext context)
		{
			_context = context;

			lock (_lock)
			{
				if (_callbacks == null)
				{
					return;
				}

				foreach (var item in _callbacks)
				{
					if (!item.cancellationToken.IsCancellationRequested)
					{
						item.callback.Invoke(context);
					}
				}

				_callbacks.Clear();
			}
		}
	}
}
