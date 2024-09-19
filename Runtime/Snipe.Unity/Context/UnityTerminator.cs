using UnityEngine;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MiniIT.Snipe
{
	internal static class UnityTerminator
	{
		private static HashSet<WeakReference<IDisposable>> s_references;
		private static readonly object s_lock = new object();

		internal static void AddTarget(IDisposable disposable)
		{
			lock (s_lock)
			{
				if (s_references == null)
				{
					s_references = new HashSet<WeakReference<IDisposable>>();
					Application.quitting += OnApplicationQuit;
#if UNITY_EDITOR
					EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
#endif
				}

				s_references.Add(new WeakReference<IDisposable>(disposable));
			}
		}

#if UNITY_EDITOR
		private static void OnEditorPlayModeStateChanged(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.ExitingPlayMode)
			{
				OnApplicationQuit();
			}
		}
#endif

		private static void OnApplicationQuit()
		{
			Application.quitting -= OnApplicationQuit;
#if UNITY_EDITOR
			EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
#endif

			DisposeTargets();
		}

		private static void DisposeTargets()
		{
			lock (s_lock)
			{
				foreach (var weak in s_references)
				{
					if (weak.TryGetTarget(out IDisposable disposable))
					{
						disposable?.Dispose();
					}
				}

				s_references.Clear();
			}
		}
	}
}
