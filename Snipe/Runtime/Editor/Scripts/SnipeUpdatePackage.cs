#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

public class SnipeUpdatePackage
{
	private static AddRequest mRequest;

	[MenuItem("Snipe/Force Update Package")]
	public static void RemoveSnipeLockSection()
	{
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