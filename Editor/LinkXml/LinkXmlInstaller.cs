// Unity does not look for link.xml files in referenced UPM packages.
// This solution was found here:
// https://forum.unity.com/threads/the-current-state-of-link-xml-in-packages.995848/#post-6545491
//
// Alternatives are listed here:
// https://github.com/jilleJr/Newtonsoft.Json-for-Unity/wiki/Embed-link.xml-in-UPM-package


#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace MiniIT.Snipe.Editor
{
	public class LinkXmlInstaller : IUnityLinkerProcessor
	{
		int IOrderedCallback.callbackOrder => 0;
		
		string IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
		{
			// This is pretty ugly, but it was the only thing I could think of in order to reliably get the path to link.xml
			const string linkXmlGuid = "60ff61abd6fe4ff48bb8ea7a8104cadd"; // copied from link.xml.meta
			
			var assetPath = AssetDatabase.GUIDToAssetPath(linkXmlGuid);
			// assets paths are relative to the unity project root, but they don't correspond to actual folders for
			// Packages that are embedded. I.e. it won't work if a package is installed as a git submodule
			// So resolve it to an absolute path:
			return Path.GetFullPath(assetPath);
		}
		
#if !UNITY_2021_2_OR_NEWER
		void IUnityLinkerProcessor.OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data)
		{
		}
		void IUnityLinkerProcessor.OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data)
		{
		}
#endif
	}
}

#endif
 