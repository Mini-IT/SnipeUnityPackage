#if UNITY_EDITOR

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using Google;

public class SnipeUpdatePackage
{
	private static AddRequest mRequest;

	[MenuItem("Snipe/Force Update Package")]
	public static void RemoveSnipeLockSection()
	{
		/*
		string directory_path = Directory.GetParent(Application.dataPath).FullName;

		string file_path = $"{directory_path}{Path.DirectorySeparatorChar}Packages{Path.DirectorySeparatorChar}manifest.json";
		
		var reader = File.OpenText(file_path);

		Dictionary<string, object> data = new Dictionary<string, object>();
		EditorJsonUtility.FromJsonOverwrite(reader.ReadToEnd(), data);
		//ExpandoObject data = ExpandoObject.FromJSONString(reader.ReadToEnd());
		reader.Close();

		if (data?["lock"] is Dictionary<string, object> lock_section)
		{
			if (lock_section.Remove("com.miniit.snipe.client"))
			{
				string json = EditorJsonUtility.ToJson(data);
				if (!string.IsNullOrEmpty(json))
				{
					string bak_file_path = $"{file_path}_{DateTime.Now.ToShortTimeString().Replace(":", ".")}_{DateTime.Now.ToShortDateString()}.bak";

					//File.Move(file_path, bak_file_path);
					var writer = File.OpenWrite(file_path.Replace("manifest", "exp"));
					byte[] bytes = Encoding.UTF8.GetBytes(json);
					writer.Write(bytes, 0, bytes.Length);
					writer.Close();
				}
			}
		}
		*/

		if (mRequest != null)
			return;

		mRequest = Client.Add("https://github.com/Mini-IT/SnipeUnityPackage.git");
		EditorApplication.update += OnEditorUpdate;
	}

	private static void OnEditorUpdate()
	{
		if (mRequest != null)
		{
			if (mRequest.IsCompleted)
			{
				if (mRequest.Status == StatusCode.Success)
					Debug.Log("Installed: " + mRequest.Result.packageId);
				else if (mRequest.Status >= StatusCode.Failure)
					Debug.Log(mRequest.Error.message);

				mRequest = null;
				EditorApplication.update -= OnEditorUpdate;
			}
		}
		else
		{
			EditorApplication.update -= OnEditorUpdate;
		}
	}
}

#endif // UNITY_EDITOR