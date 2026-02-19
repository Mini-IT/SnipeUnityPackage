using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public abstract class AuthIdFetcher : IDisposable
	{
		public string Value { get; protected set; }

		protected ISnipeServices Services { get; private set; }

		protected CancellationTokenSource _cts = new CancellationTokenSource();
		
		public abstract void Fetch(bool waitInitialization, Action<string> callback = null);

		internal void SetServices(ISnipeServices services)
		{
			Services = services ?? throw new ArgumentNullException(nameof(services));
		}

		public virtual void Dispose()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
