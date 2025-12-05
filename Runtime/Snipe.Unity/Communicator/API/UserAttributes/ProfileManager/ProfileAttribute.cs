using System;
using System.Collections.Generic;
using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	public interface IProfileAttribute : IDisposable
	{
	}

	public class ProfileAttribute<T> : IProfileAttribute, IObservable<T>
	{
		public T Value
		{
			get => _value;
			set
			{
				if (Equals(_value, value))
				{
					return;
				}

				_value = value;
				_manager?.OnLocalAttributeChanged(_key, value);
				NotifyObservers(value);
			}
		}

		public event Action<T> ValueChanged;

		private T _value;
		private readonly string _key;
		private readonly ProfileManager _manager;
		private readonly ISharedPrefs _sharedPrefs;
		private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();

		internal ProfileAttribute(string key, ProfileManager manager, ISharedPrefs sharedPrefs)
		{
			_key = key;
			_manager = manager;
			_sharedPrefs = sharedPrefs;
		}

		public void SetValueFromServer(T value)
		{
			if (Equals(_value, value))
			{
				return;
			}

			_value = value;
			NotifyObservers(value);
		}

		public IDisposable Subscribe(IObserver<T> observer)
		{
			if (!_observers.Contains(observer))
			{
				_observers.Add(observer);
			}
			return this;
		}

		public void Dispose()
		{
			_observers.Clear();
			ValueChanged = null;
		}

		public ProfileAttribute<T> Migrate(string oldKey)
		{
			if (!_sharedPrefs.HasKey(oldKey))
			{
				return this;
			}

			string prefsKey = ProfileManager.KEY_ATTR_PREFIX + _key;
			if (oldKey == prefsKey)
			{
				return this;
			}

			var value = _manager.GetPrefsValue<T>(oldKey);
			_value = value;
			_manager.SetLocalValue(_key, value);

			_sharedPrefs.DeleteKey(oldKey);
			return this;
		}

		private void NotifyObservers(T value)
		{
			ValueChanged?.Invoke(value);
			foreach (var observer in _observers)
			{
				observer.OnNext(value);
			}
		}

		public static implicit operator T(ProfileAttribute<T> attr)
		{
			return attr.Value;
		}

		public override string ToString() => Value?.ToString() ?? string.Empty;
	}
}


