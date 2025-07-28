using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public abstract class AuthIdFetcher : IDisposable
	{
		public string Value { get; protected set; }

		protected CancellationTokenSource _cts = new CancellationTokenSource();
		
		public abstract void Fetch(bool waitInitialization, Action<string> callback = null);

		public virtual void Dispose()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
