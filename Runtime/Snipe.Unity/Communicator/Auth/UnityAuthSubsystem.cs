using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Unity
{
	public class UnityAuthSubsystem : AuthSubsystem
	{
		public UnityAuthSubsystem(int contextId, SnipeCommunicator communicator, SnipeConfig config, SnipeAnalyticsTracker analytics)
			: base(contextId, communicator, config, analytics)
		{
		}

		protected override void RegisterDefaultBindings()
		{
			if (!TryGetBinding<DeviceIdBinding>(false, out _))
			{
				_bindings.Add(new DeviceIdBinding(_communicator, this, _config));
			}

			if (!TryGetBinding<AdvertisingIdBinding>(false, out _))
			{
				_bindings.Add(new AdvertisingIdBinding(_communicator, this, _config));
			}

#if SNIPE_FACEBOOK
			if (!TryGetBinding<FacebookBinding>(false, out _))
			{
				_bindings.Add(new FacebookBinding(_communicator, this, _config));
			}
#endif

#if UNITY_ANDROID
			if (!TryGetBinding<AmazonBinding>(false, out _))
			{
				_bindings.Add(new AmazonBinding(_communicator, this, _config));
			}
#endif
		}

		protected override async void RegisterAndLogin()
		{
			if (_communicator == null)
			{
				return;
			}

			var providers = new List<IDictionary<string, object>>();

			if (_bindings.Count > 0)
			{
				var tasks = new List<UniTask>(2);

				foreach (AuthBinding binding in _bindings)
				{
					if (binding?.Fetcher != null)
					{
						if (binding is DeviceIdBinding or AdvertisingIdBinding)
						{
							tasks.Add(FetchLoginId(binding.ProviderId, binding.Fetcher, providers, true));
						}
#if NUTAKU
						else if (binding is NutakuBinding)
						{
							tasks.Add(FetchLoginId(binding.ProviderId, binding.Fetcher, providers, false));
						}
#endif
					}
				}

				await UniTask.WhenAll(tasks.ToArray());
			}

			RequestRegisterAndLogin(providers);
		}
	}
}
