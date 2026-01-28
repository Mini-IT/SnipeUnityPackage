using System;
using MiniIT.Integrations.Nutaku;

namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuIdFetcher : AuthIdFetcher, IAuthIdFetcherWithToken
	{
		public string Token { get; private set; }

		private Action<string> _callback;
		private readonly INutakuAdapter _nutakuAdapter;

		public NutakuIdFetcher()
			: this(NutakuAdapterLocator.Instance)
		{
		}

		private NutakuIdFetcher(INutakuAdapter nutakuAdapter)
		{
			_nutakuAdapter = nutakuAdapter;
		}

		public override void Fetch(bool waitInitialization, Action<string> callback = null)
		{
			_callback = callback;
			_nutakuAdapter.FetchAuthData(waitInitialization, OnAuthDataReceived);
		}

		private void OnAuthDataReceived(NutakuAuthData data)
		{
			SetValue(data.UserId);
			SetToken(data.Token);
			InvokeCallback(Value);
		}

		private void SetToken(string value)
		{
			Token = CheckValueValid(value) ? value : "";
		}

		private void SetValue(string value)
		{
			Value = CheckValueValid(value) ? value : "";
		}

		private static bool CheckValueValid(string value)
		{
			return !string.IsNullOrEmpty(value);
		}

		private void InvokeCallback(string value)
		{
			_callback?.Invoke(value);
		}
	}
}
