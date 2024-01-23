using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public class UnityApplicationInfo : IApplicationInfo
	{
		public string ApplicationIdentifier { get; }
		public string ApplicationVersion { get; }
		public string ApplicationPlatform { get; }
		public string DeviceIdentifier { get; }
		public string PersistentDataPath { get; }
		public string StreamingAssetsPath { get; }

		public UnityApplicationInfo()
		{
			ApplicationIdentifier = Application.identifier;
			ApplicationVersion = Application.version;

#if AMAZON_STORE && !UNITY_EDITOR
			ApplicationPlatform = Application.platform.ToString() + "Amazon";
#else
			ApplicationPlatform = Application.platform.ToString();
#endif

			DeviceIdentifier = SystemInfo.deviceUniqueIdentifier;
			PersistentDataPath = Application.persistentDataPath;
			StreamingAssetsPath = Application.streamingAssetsPath;
		}
	}
}
