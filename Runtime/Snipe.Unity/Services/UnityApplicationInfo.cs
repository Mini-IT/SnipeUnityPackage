using MiniIT.Snipe;
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
			ApplicationPlatform = Application.platform.ToString();
			DeviceIdentifier = SystemInfo.deviceUniqueIdentifier;
			PersistentDataPath = Application.persistentDataPath;
			StreamingAssetsPath = Application.streamingAssetsPath;
		}
	}
}
