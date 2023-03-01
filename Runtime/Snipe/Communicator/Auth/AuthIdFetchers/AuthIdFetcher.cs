using System;

namespace MiniIT.Snipe
{
	public abstract class AuthIdFetcher
	{
		public string Value { get; protected set; }

		public abstract void Fetch(bool wait_initialization, Action<string> callback = null);
	}
}