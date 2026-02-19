using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.Storage;

namespace MiniIT.Snipe
{
	internal sealed class AuthBindingManager
	{
		private readonly int _contextId;
		private readonly ISnipeCommunicator _communicator;
		private readonly AuthSubsystem _authSubsystem;
		private readonly ISharedPrefs _sharedPrefs;
		private readonly ILogger _logger;
		private SnipeOptions _options;
		private readonly HashSet<AuthBinding> _bindings = new ();

		public AuthBindingManager(
			int contextId,
			ISnipeCommunicator communicator,
			AuthSubsystem authSubsystem,
			SnipeOptions options,
			ISharedPrefs sharedPrefs,
			ILogger logger)
		{
			_contextId = contextId;
			_communicator = communicator;
			_authSubsystem = authSubsystem;
			_options = options;
			_sharedPrefs = sharedPrefs;
			_logger = logger;
		}

		public void Reconfigure(SnipeOptions options)
		{
			_options = options;
		}

		public IEnumerable<AuthBinding> Bindings => _bindings;

		public int BindingCount => _bindings.Count;

		public TBinding RegisterBinding<TBinding>(TBinding binding) where TBinding : AuthBinding
		{
			binding.Initialize(_contextId, _communicator, _authSubsystem, () => _options.ClientKey);
			_bindings.Add(binding);
			return binding;
		}

		public bool TryGetBinding<TBinding>(bool searchBaseClasses, out TBinding binding) where TBinding : AuthBinding
		{
			Type targetBindingType = typeof(TBinding);
			binding = null;

			if (_bindings.Count <= 0)
			{
				return false;
			}

			foreach (var registeredBinding in _bindings)
			{
				if (registeredBinding != null && registeredBinding.GetType() == targetBindingType)
				{
					binding = registeredBinding as TBinding;
					break;
				}
			}

			if (binding != null)
			{
				return true;
			}

			// if no exact type match found, try base classes
			if (!searchBaseClasses)
			{
				return false;
			}

			foreach (var registeredBinding in _bindings)
			{
				if (registeredBinding is TBinding b)
				{
					binding = b;
					break;
				}
			}

			return binding != null;
		}

		public void StartBindings()
		{
			_logger.LogInformation("StartBindings");

			foreach (var binding in _bindings)
			{
				binding?.Start();
			}
		}

		public void BindAll()
		{
			foreach (var binding in _bindings)
			{
				binding?.Bind();
			}
		}

		public void ClearAllBindings()
		{
			foreach (var binding in _bindings)
			{
				binding.IsBindDone = false;
			}
		}

		public void MarkBindingDone(string provider)
		{
			AuthBinding binding = FindBindingByProvider(provider);
			if (binding != null)
			{
				binding.IsBindDone = true;
				return;
			}

			_sharedPrefs.SetInt(SnipePrefs.GetAuthBindDone(_contextId) + provider, 1);
		}

		public void HandleAccountBindingCollision(
			AuthBinding binding,
			string username,
			bool automaticallyBindCollisions,
			AuthSubsystem.AccountBindingCollisionHandler collisionHandler)
		{
			if (automaticallyBindCollisions)
			{
				binding.Bind();
			}
			else
			{
				collisionHandler?.Invoke(binding, username);
			}
		}

		public void Dispose()
		{
			foreach (var binding in _bindings)
			{
				binding?.Dispose();
			}

			_bindings.Clear();
		}

		private AuthBinding FindBindingByProvider(string provider)
		{
			foreach (var binding in _bindings)
			{
				if (binding == null)
				{
					continue;
				}

				if (binding.ProviderId == provider)
				{
					return binding;
				}
			}

			return null;
		}
	}
}
