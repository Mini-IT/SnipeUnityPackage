using System;
using System.Collections.Generic;
using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	// Provides a ProfileAttribute-like interface thus not being a real user attribute
	public sealed class LocalProfileAttribute<T> : AbstractProfileAttribute, IProfileAttribute<T>
	{
		public T Value
		{
			get => _prefsHelper.GetPrefsValue<T>(_key, _defaultValue);
			set
			{
				T oldValue = _prefsHelper.GetPrefsValue<T>(_key);
				if (Equals(oldValue, value))
				{
					return;
				}

				_prefsHelper.SetLocalValue(_key, value);
				NotifyObservers(value);
			}
		}

		public event Action<T> ValueChanged;

		private readonly T _defaultValue;
		private readonly PlayerPrefsTypeHelper _prefsHelper;
		private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();

		public LocalProfileAttribute(string key, ISharedPrefs sharedPrefs, T defaultValue = default)
			: base(key, sharedPrefs)
		{
			_defaultValue = defaultValue;
			_prefsHelper = new PlayerPrefsTypeHelper(sharedPrefs);
		}

		public IDisposable Subscribe(IObserver<T> observer)
		{
			if (!_observers.Contains(observer))
			{
				_observers.Add(observer);
			}
			return this;
		}

		public override void Dispose()
		{
			_observers.Clear();
			ValueChanged = null;
		}

		private void NotifyObservers(T value)
		{
			ValueChanged?.Invoke(value);
			foreach (var observer in _observers)
			{
				observer.OnNext(value);
			}
		}

		public static implicit operator T(LocalProfileAttribute<T> attr)
		{
			return attr.Value;
		}

		public override string ToString() => Value?.ToString() ?? string.Empty;
	}
}
