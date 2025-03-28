using System;

namespace MiniIT.Snipe
{
	public class AuthIdObserver : IObserver<string>
	{
		public string Value { get; private set; }

		public void OnNext(string value)
		{
			Value = value;
		}

		public void OnCompleted()
		{
			// ignore
		}

		public void OnError(Exception error)
		{
			// ignore
		}
	}
}
