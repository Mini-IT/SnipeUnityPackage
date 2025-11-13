using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Unity
{
	public class UnityAuthSubsystem : AuthSubsystem
	{
		public UnityAuthSubsystem(SnipeCommunicator communicator, SnipeConfig config)
			: base(communicator, config)
		{
		}

		protected override void InitDefaultBindings()
		{
#if UNITY_WEBGL
			if (FindBinding<WebGLBinding>(false) == null)
			{
				_bindings.Add(new WebGLBinding(_communicator, this, _config));
			}
#endif

			if (FindBinding<DeviceIdBinding>(false) == null)
			{
				_bindings.Add(new DeviceIdBinding(_communicator, this, _config));
			}

			if (FindBinding<AdvertisingIdBinding>(false) == null)
			{
				_bindings.Add(new AdvertisingIdBinding(_communicator, this, _config));
			}

#if SNIPE_FACEBOOK
			if (FindBinding<FacebookBinding>(false) == null)
			{
				_bindings.Add(new FacebookBinding(_communicator, this, _config));
			}
#endif

#if UNITY_ANDROID
			if (FindBinding<AmazonBinding>(false) == null)
			{
				_bindings.Add(new AmazonBinding(_communicator, this, _config));
			}
#endif
		}

		protected override async UniTaskVoid RegisterAndLogin()
		{
			if (_communicator == null)
			{
				return;
			}

			var providers = new List<SnipeObject>();

			if (_bindings.Count > 0)
			{
				var tasks = new List<UniTask>(3);

				foreach (AuthBinding binding in _bindings)
				{
					if (binding?.Fetcher == null)
					{
						continue;
					}

#if UNITY_WEBGL
					if (binding is WebGLBinding)
                    {
						tasks.Add(FetchLoginId(binding.ProviderId, binding.Fetcher, providers, false));
                    }
#else

					if (binding is DeviceIdBinding or AdvertisingIdBinding)
					{
						tasks.Add(FetchLoginId(binding.ProviderId, binding.Fetcher, providers, true));
					}
					else if (binding.Fetcher is IAuthIdFetcherWithToken)
					{
						tasks.Add(FetchLoginId(binding.ProviderId, binding.Fetcher, providers, false));
					}
#endif
				}

				await UniTask.WhenAll(tasks.ToArray());
			}

			RequestRegisterAndLogin(providers);
		}
	}
}
