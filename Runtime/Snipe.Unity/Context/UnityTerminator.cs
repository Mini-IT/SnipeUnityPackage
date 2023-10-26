using UnityEngine;

namespace MiniIT.Snipe
{
	internal static class UnityTerminator
	{
		private static bool s_initialized = false;
		private static readonly object s_lock = new object();

		internal static void Run()
		{
			lock (s_lock)
			{
				if (!s_initialized)
				{
					s_initialized = true;
					Application.quitting += OnApplicationQuit;
				}
			}
		}

		private static void OnApplicationQuit()
		{
			Application.quitting -= OnApplicationQuit;

			foreach (var context in SnipeContext.All)
			{
				context?.Dispose();
			}
		}
	}
}
