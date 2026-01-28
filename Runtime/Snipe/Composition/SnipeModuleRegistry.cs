using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public sealed class SnipeModuleRegistry : ISnipeModuleRegistry
	{
		private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

		public void Register<T>(T instance) where T : class
		{
			if (instance == null)
			{
				throw new ArgumentNullException(nameof(instance));
			}

			var type = typeof(T);
			if (_services.ContainsKey(type))
			{
				throw new InvalidOperationException($"Module `{type.Name}` is already registered.");
			}

			_services.Add(type, instance);
		}

		public bool TryGet<T>(out T instance) where T : class
		{
			if (_services.TryGetValue(typeof(T), out var value))
			{
				instance = value as T;
				return instance != null;
			}

			instance = null;
			return false;
		}

		public T Get<T>() where T : class
		{
			if (TryGet<T>(out var instance))
			{
				return instance;
			}

			throw new InvalidOperationException($"Module `{typeof(T).Name}` is not registered.");
		}
	}
}
