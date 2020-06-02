#if UNITY_EDITOR

using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using MiniIT;

public class SnipeApiDownloader : EditorWindow
{
	private string mProjectId = "1";
	private string mDirectoryPath;
	private string mLogin;
	private string mPassword;

	private string mPrefsPrefix;

	[MenuItem("Snipe/Download SnipeApi")]
	public static void ShowWindow()
	{
		EditorWindow.GetWindow(typeof(SnipeApiDownloader));
	}

	protected void OnEnable()
	{
		if (string.IsNullOrEmpty(mPrefsPrefix))
		{
			var hash = System.Security.Cryptography.MD5.Create().ComputeHash(UTF8Encoding.UTF8.GetBytes(Application.dataPath));
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < hash.Length; i++)
			{
				builder.Append(hash[i].ToString("x2"));
			}
			mPrefsPrefix = builder.ToString();
		}

		mProjectId = EditorPrefs.GetString($"{mPrefsPrefix}_SnipeApiDownloader.project_id", mProjectId);
		mDirectoryPath = EditorPrefs.GetString($"{mPrefsPrefix}_SnipeApiDownloader.directory", mDirectoryPath);
		mLogin = EditorPrefs.GetString($"{mPrefsPrefix}_SnipeApiDownloader.login", mLogin);
		mPassword = EditorPrefs.GetString($"{mPrefsPrefix}_SnipeApiDownloader.password", mPassword);

		if (string.IsNullOrEmpty(mDirectoryPath))
			mDirectoryPath = Application.dataPath;
	}

	protected void OnDisable()
	{
		EditorPrefs.SetString($"{mPrefsPrefix}_SnipeApiDownloader.project_id", mProjectId);
		EditorPrefs.SetString($"{mPrefsPrefix}_SnipeApiDownloader.directory", mDirectoryPath);
		EditorPrefs.SetString($"{mPrefsPrefix}_SnipeApiDownloader.login", mLogin);
		EditorPrefs.SetString($"{mPrefsPrefix}_SnipeApiDownloader.password", mPassword);
	}

	void OnGUI()
	{
		mProjectId = EditorGUILayout.TextField("Project ID", mProjectId);

		GUILayout.BeginHorizontal();
		mDirectoryPath = EditorGUILayout.TextField("Directory", mDirectoryPath);
		if (GUILayout.Button("..."))
		{
			string path = EditorUtility.SaveFolderPanel("Choose location of SnipeApi.cs", mDirectoryPath, "");
			if (!string.IsNullOrEmpty(path))
			{
				mDirectoryPath = path;
			}
		}
		GUILayout.EndHorizontal();

		mLogin = EditorGUILayout.TextField("Login", mLogin);
		mPassword = EditorGUILayout.TextField("Password", mPassword);

		EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(mLogin) || string.IsNullOrEmpty(mPassword));
		if (GUILayout.Button("Download"))
		{
			DownloadSnipeApi();
			this.Close();
		}
		EditorGUI.EndDisabledGroup();
	}

	public void DownloadSnipeApi()
	{
		UnityEngine.Debug.Log("DownloadSnipeApi - start");

		Process process = new Process();
		process.StartInfo.WorkingDirectory = Application.dataPath + "/..";
		process.StartInfo.FileName = "curl";
		process.StartInfo.Arguments = $"-s -X POST \"https://edit-dev.snipe.dev/api/v1/auth\" -d \"login={mLogin}&password={mPassword}\"";
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.RedirectStandardOutput = true;
		process.Start();

		StreamReader reader = process.StandardOutput;
		string output = reader.ReadToEnd();

		UnityEngine.Debug.Log("output " + output);

		ExpandoObject response = ExpandoObject.FromJSONString(output);
		string token = response?.SafeGetString("token");
		if (string.IsNullOrEmpty(token))
		{
			UnityEngine.Debug.Log("DownloadSnipeApi - FAILED to get token");
			return;
		}

		process = new Process();
		process.StartInfo.WorkingDirectory = mDirectoryPath;
		process.StartInfo.FileName = "curl";
		process.StartInfo.Arguments = $"-o SnipeApi.cs -H \"Authorization: Bearer {token}\" \"https://edit-dev.snipe.dev/api/v1/project/{mProjectId}/code/unityBindings\"";
		process.Start();

		UnityEngine.Debug.Log("DownloadSnipeApi - done");
	}
}

#endif // UNITY_EDITOR