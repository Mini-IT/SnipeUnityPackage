#if UNITY_EDITOR

using System;
using System.IO;
using UnityEngine;
using UnityEditor;

public class SnipeInitAssemblyDefinitions
{
	[MenuItem("Snipe/Initialize Assembly Definitions")]
	public static void Process()
	{
		string gpg_directory = $"{Application.dataPath}/GooglePlayGames";
		if (Directory.Exists(gpg_directory))
		{
			string asmdef_file = $"{gpg_directory}/MiniIT.GooglePlay.Runtime.asmdef";
			if (!File.Exists(asmdef_file))
			{
				var file_writer = File.CreateText(asmdef_file);
				file_writer.WriteLine("{");
				file_writer.WriteLine("\t\"name\": \"MiniIT.GooglePlay.Runtime\",");
				file_writer.WriteLine("\t\"precompiledReferences\": [],");
				file_writer.WriteLine("\t\"allowUnsafeCode\": false,");
				file_writer.WriteLine("\t\"overrideReferences\": false,");
				file_writer.WriteLine("\t\"autoReferenced\": true,");
				file_writer.WriteLine("\t\"noEngineReferences\": false");
				file_writer.WriteLine("}");
				file_writer.Close();
			}

			asmdef_file = $"{gpg_directory}/Editor/MiniIT.GooglePlay.Editor.asmdef";
			if (!File.Exists(asmdef_file))
			{
				var file_writer = File.CreateText(asmdef_file);
				file_writer.WriteLine("{");
				file_writer.WriteLine("\t\"name\": \"MiniIT.GooglePlay.Editor\",");
				file_writer.WriteLine("\t\"references\": [\"MiniIT.GooglePlay.Runtime\"],");
				file_writer.WriteLine("\t\"includePlatforms\": [\"Editor\"],");
				file_writer.WriteLine("\t\"precompiledReferences\": [],");
				file_writer.WriteLine("\t\"allowUnsafeCode\": false,");
				file_writer.WriteLine("\t\"overrideReferences\": false,");
				file_writer.WriteLine("\t\"autoReferenced\": true,");
				file_writer.WriteLine("\t\"noEngineReferences\": false");
				file_writer.WriteLine("}");
				file_writer.Close();
			}
		}

		string facebook_directory = $"{Application.dataPath}/FacebookSDK";
		if (Directory.Exists(facebook_directory))
		{
			string asmdef_file = $"{facebook_directory}/MiniIT.Facebook.Runtime.asmdef";
			if (!File.Exists(asmdef_file))
			{
				var file_writer = File.CreateText(asmdef_file);
				file_writer.WriteLine("{");
				file_writer.WriteLine("\t\"name\": \"MiniIT.Facebook.Runtime\",");
				file_writer.WriteLine("\t\"references\": [],");
				file_writer.WriteLine("\t\"includePlatforms\": [],");
				file_writer.WriteLine("\t\"precompiledReferences\": [],");
				file_writer.WriteLine("\t\"allowUnsafeCode\": false,");
				file_writer.WriteLine("\t\"overrideReferences\": false,");
				file_writer.WriteLine("\t\"autoReferenced\": true,");
				file_writer.WriteLine("\t\"noEngineReferences\": false");
				file_writer.WriteLine("}");
				file_writer.Close();
			}
		}
	}
}

#endif // UNITY_EDITOR