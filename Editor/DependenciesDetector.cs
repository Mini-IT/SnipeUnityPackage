#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEditor;

using Debug = UnityEngine.Debug;

namespace MiniIT.Snipe.Editor
{
	[InitializeOnLoad]
	public static class DependenciesDetector
	{
		private const string NAMESPACE_FACEBOOK = "Facebook.Unity";
		private const string DEFINE_FACEBOOK = "SNIPE_FACEBOOK";
		
		static DependenciesDetector()
        {
			var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (buildTargetGroup == BuildTargetGroup.Unknown)
            {
                var propertyInfo = typeof(EditorUserBuildSettings).GetProperty("activeBuildTargetGroup");//, BindingFlags.Static | BindingFlags.NonPublic);
                if (propertyInfo != null)
                    buildTargetGroup = (BuildTargetGroup)propertyInfo.GetValue(null, null);
            }
			
			var previousProjectDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            var projectDefines = previousProjectDefines.Split(';').ToList();
			
            RefreshDefineSymbolForNamespace(buildTargetGroup, projectDefines, NAMESPACE_FACEBOOK, DEFINE_FACEBOOK);
			
			PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", projectDefines.ToArray()));

			// Let other systems execute before reloading assemblies
			Thread.Sleep(1000);
			EditorApplication.UnlockReloadAssemblies();
        }
		
		private static void RefreshDefineSymbolForNamespace(BuildTargetGroup buildTargetGroup, IList<string> projectDefines, string namespace_name, string define_symbol)
		{
			if (EditorUtil.NamespaceExists(namespace_name))
			{
				if (!projectDefines.Contains(define_symbol, StringComparer.OrdinalIgnoreCase))
				{
					Debug.Log($"[Snipe DependenciesDetector] Add define symbol {define_symbol}");
					projectDefines.Add(define_symbol);
				}
			}
			else
			{
				if (projectDefines.Remove(define_symbol))
				{
					Debug.Log("[Snipe DependenciesDetector] Remove define symbol {define_symbol}");
				}
			}
		}
	}
}

#endif