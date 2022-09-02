#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor.Compilation;
using UnityEditor;

using Debug = UnityEngine.Debug;

namespace MiniIT.Snipe.Editor
{
	[InitializeOnLoad]
	public static class DependenciesDetector
	{
		private static readonly string[][] NAMESPACE_DEFINES = new []
		{
			new string[] { "Facebook.Unity", "SNIPE_FACEBOOK" },
		};
		
		static DependenciesDetector()
        {
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;
			
			Debug.Log($"[Snipe DependenciesDetector] start");
			
			DelayedRun();
		}
		
		private static async void DelayedRun()
		{
			await Task.Delay(200);
			Debug.Log($"[Snipe DependenciesDetector] delay finished");
			
			//CompilationPipeline.compilationFinished += (context) =>
            //{
			//	Debug.Log($"[Snipe DependenciesDetector] compilationFinished");
				
				var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
				if (buildTargetGroup == BuildTargetGroup.Unknown)
				{
					var propertyInfo = typeof(EditorUserBuildSettings).GetProperty("activeBuildTargetGroup");//, BindingFlags.Static | BindingFlags.NonPublic);
					if (propertyInfo != null)
						buildTargetGroup = (BuildTargetGroup)propertyInfo.GetValue(null, null);
				}
				
				Debug.Log($"[Snipe DependenciesDetector] buildTargetGroup = {buildTargetGroup}");
				
				EditorApplication.LockReloadAssemblies();
				
				var previousProjectDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
				var projectDefines = previousProjectDefines.Split(';').ToList();
				
				foreach (var item in NAMESPACE_DEFINES)
				{
					RefreshDefineSymbolForNamespace(buildTargetGroup, projectDefines, item[0], item[1]);
				}
				
				var newProjectDefines = string.Join(";", projectDefines.ToArray());
				if (newProjectDefines != previousProjectDefines)
				{
					Debug.Log($"[Snipe DependenciesDetector] define symbols changed - applying");
					
					//EditorApplication.LockReloadAssemblies();
					
					PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newProjectDefines);
					
					// Let other systems execute before reloading assemblies
					Thread.Sleep(1000);
				}
				
				EditorApplication.UnlockReloadAssemblies();
			//};
        }
		
		private static void RefreshDefineSymbolForNamespace(BuildTargetGroup buildTargetGroup, IList<string> projectDefines, string namespace_name, string define_symbol)
		{
			Debug.Log($"[Snipe DependenciesDetector] RefreshDefineSymbolForNamespace {buildTargetGroup} - {define_symbol} - {namespace_name}");
			
			if (EditorUtil.NamespaceExists(namespace_name))
			{
				Debug.Log($"[Snipe DependenciesDetector] -- namespace exists {namespace_name}");
				if (!projectDefines.Contains(define_symbol, StringComparer.OrdinalIgnoreCase))
				{
					Debug.Log($"[Snipe DependenciesDetector] Add define symbol {define_symbol}");
					projectDefines.Add(define_symbol);
				}
				else
				{
					Debug.Log($"[Snipe DependenciesDetector] -- define symbol exists {define_symbol}");
				}
			}
			else
			{
				Debug.Log($"[Snipe DependenciesDetector] -- namespace does not exist {namespace_name}");
				
				if (projectDefines.Remove(define_symbol))
				{
					Debug.Log("[Snipe DependenciesDetector] Remove define symbol {define_symbol}");
				}
			}
		}
	}
}

#endif