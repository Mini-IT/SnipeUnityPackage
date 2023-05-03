using UnityEngine;

namespace MiniIT.Snipe
{
	internal class UnityTerminator : MonoBehaviour
	{
		private static UnityTerminator _instance;

		internal static void Run()
		{
			if (_instance != null)
				return;

			var go = new GameObject();
			go.hideFlags = HideFlags.HideAndDontSave;
			_instance = go.AddComponent<UnityTerminator>();
		}

		private void OnApplicationQuit()
		{
			foreach (var context in SnipeContext.All)
			{
				context?.Dispose();
			}
		}
	}
}
