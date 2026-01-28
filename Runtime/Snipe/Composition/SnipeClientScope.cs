using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public sealed class SnipeClientScope : IDisposable
	{
		public ISnipeServices Services { get; }

		private readonly SnipeModuleRegistry _registry;
		private readonly List<ISnipeModule> _modules;

		internal SnipeClientScope(ISnipeServices services, SnipeModuleRegistry registry, List<ISnipeModule> modules)
		{
			Services = services ?? throw new ArgumentNullException(nameof(services));
			_registry = registry ?? throw new ArgumentNullException(nameof(registry));
			_modules = modules ?? throw new ArgumentNullException(nameof(modules));
		}

		public bool TryGetModule<T>(out T instance) where T : class
		{
			return _registry.TryGet(out instance);
		}

		public T GetModule<T>() where T : class
		{
			return _registry.Get<T>();
		}

		public void Dispose()
		{
			for (int i = 0; i < _modules.Count; i++)
			{
				if (_modules[i] is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}

			if (Services is IDisposable servicesDisposable)
			{
				servicesDisposable.Dispose();
			}
		}
	}
}
