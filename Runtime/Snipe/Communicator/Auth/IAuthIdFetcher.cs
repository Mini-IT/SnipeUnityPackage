using System;

namespace MiniIT.Snipe
{
	public interface IAuthIdFetcher
	{
		string Value { get; }
		void Fetch(bool waitInitialization, Action<string> callback = null);
	}
}
