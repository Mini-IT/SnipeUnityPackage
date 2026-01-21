using System;

namespace MiniIT.Snipe
{
	public abstract class AuthIdFetcher : IAuthIdFetcher
	{
		public string Value { get; protected set; }

		public abstract void Fetch(bool waitInitialization, Action<string> callback = null);
	}
}
