using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public class UnityInternetReachability : IInternetReachability
	{
		public bool IsInternetAvailable => Application.internetReachability != NetworkReachability.NotReachable;
	}
}
