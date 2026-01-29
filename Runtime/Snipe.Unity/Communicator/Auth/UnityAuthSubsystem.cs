using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe;

namespace MiniIT.Snipe.Unity
{
	public class UnityAuthSubsystem : AuthSubsystem
	{
		public UnityAuthSubsystem(int contextId, SnipeCommunicator communicator, ISnipeAnalyticsTracker analytics, ISnipeServices services)
			: base(contextId, communicator, analytics, services)
		{
		}

		protected override async UniTaskVoid RegisterAndLogin()
		{
			if (_communicator == null)
			{
				return;
			}

			var providers = new Dictionary<string, Dictionary<string, object>>();

			if (_bindings.Count > 0)
			{
				var tasks = new List<UniTask>(3);

				foreach (AuthBinding binding in _bindings)
				{
					if (binding?.Fetcher == null)
					{
						continue;
					}

					if (binding is DeviceIdBinding or AdvertisingIdBinding)
					{
						tasks.Add(FetchLoginId(binding, providers));
					}
					else if (binding.Fetcher is IAuthIdFetcherWithToken)
					{
						tasks.Add(FetchLoginId(binding, providers));
					}
				}

				await UniTask.WhenAll(tasks.ToArray());
			}

			RequestRegisterAndLogin(providers);
		}
	}
}
