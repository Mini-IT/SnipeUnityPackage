using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public class UnityInternetReachabilityProvider : IInternetReachabilityProvider
	{
		public bool IsInternetAvailable => Application.internetReachability != NetworkReachability.NotReachable;
	}
}
