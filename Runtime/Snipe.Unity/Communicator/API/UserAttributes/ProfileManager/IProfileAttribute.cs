using System;

namespace MiniIT.Snipe.Api
{
	public interface IProfileAttribute<T> : IObservable<T>, IDisposable
	{
		T Value { get; set; }
		event Action<T> ValueChanged;
	}
}
