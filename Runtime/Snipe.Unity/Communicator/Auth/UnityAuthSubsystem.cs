using System.Collections.Generic;
using System.Threading.Tasks;
using MiniIT.Threading.Tasks;

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

			if (FindBinding<AmazonBinding>(false) == null)
			{
				_bindings.Add(new AmazonBinding(_communicator, this, _config));
			}
		}

		protected override async void RegisterAndLogin()
		{
			if (_communicator == null)
			{
				return;
			}

			var providers = new List<SnipeObject>();

			if (_bindings.Count > 0)
			{
				var tasks = new List<AlterTask>(2);

				foreach (AuthBinding binding in _bindings)
				{
					if (binding?.Fetcher != null && (binding is DeviceIdBinding || binding is AdvertisingIdBinding))
					{
						tasks.Add(FetchLoginId(binding.ProviderId, binding.Fetcher, providers));
					}
				}

				await AlterTask.WhenAll(tasks.ToArray());
			}

			RequestRegisterAndLogin(providers);
		}
	}
}
