using System;
using System.Collections.Generic;

#if MINIIT_SHARED_PREFS
using MiniIT.Storage;
#else
using SharedPrefs = UnityEngine.PlayerPrefs;
#endif

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
		private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();

		internal ProfileAttribute(string key, ProfileManager manager)
		{
			_key = key;
			_manager = manager;
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

		public void Migrate(string oldKey)
		{
			if (!SharedPrefs.HasKey(oldKey))
			{
				return;
			}

			var value = _manager.GetPrefsValue<T>(oldKey);
			_manager.SetLocalValue(_key, value);

			SharedPrefs.DeleteKey(oldKey);
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


