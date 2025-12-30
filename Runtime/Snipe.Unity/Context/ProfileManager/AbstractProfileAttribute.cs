using System;
using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractProfileAttribute : IDisposable
	{
		protected readonly string _key;
		protected readonly ISharedPrefs _sharedPrefs;

		internal AbstractProfileAttribute(string key, ISharedPrefs sharedPrefs)
		{
			_key = key;
			_sharedPrefs = sharedPrefs;
		}

		public abstract void Dispose();
	}
}
