using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public sealed class SnipeClientBuilder
	{
		private ISnipeServices _services;
		private readonly List<ISnipeModule> _modules = new List<ISnipeModule>();

		public SnipeClientBuilder UseServices(ISnipeServices services)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			return this;
		}

		public SnipeClientBuilder AddModule(ISnipeModule module)
		{
			if (module == null)
			{
				throw new ArgumentNullException(nameof(module));
			}

			_modules.Add(module);
			return this;
		}

		public SnipeClientScope Build()
		{
			if (_services == null)
			{
				throw new InvalidOperationException("Snipe services are not configured. Call UseServices() first.");
			}

			var registry = new SnipeModuleRegistry();
			for (int i = 0; i < _modules.Count; i++)
			{
				_modules[i].Register(registry);
			}

			return new SnipeClientScope(_services, registry, _modules);
		}
	}
}
