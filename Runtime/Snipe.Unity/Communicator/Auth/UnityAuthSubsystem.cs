using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Unity
{
	public static class AuthSubsystemExt
	{
		public static void RegisterDefaultBindings(this AuthSubsystem auth)
		{
			if (!auth.TryGetBinding<DeviceIdBinding>(false, out _))
			{
				auth.RegisterBinding(new DeviceIdBinding());
			}

			if (!auth.TryGetBinding<AdvertisingIdBinding>(false, out _))
			{
				auth.RegisterBinding(new AdvertisingIdBinding());
			}

#if SNIPE_FACEBOOK
			if (!auth.TryGetBinding<FacebookBinding>(false, out _))
			{
				auth.RegisterBinding(new FacebookBinding());
			}
#endif

#if UNITY_ANDROID
			if (!auth.TryGetBinding<AmazonBinding>(false, out _))
			{
				auth.RegisterBinding(new AmazonBinding());
			}
#endif
		}
	}

	public class UnityAuthSubsystem : AuthSubsystem
	{
		public UnityAuthSubsystem(int contextId, SnipeCommunicator communicator, SnipeConfig config, SnipeAnalyticsTracker analytics)
			: base(contextId, communicator, config, analytics)
		{
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
