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

		public string DeviceManufacturer { get; }
		public string OperatingSystemFamily { get; }
		public string OperatingSystemVersion { get; }

		public UnityApplicationInfo()
		{
			ApplicationIdentifier = Application.identifier;
			ApplicationVersion = Application.version;
			ApplicationPlatform = Application.platform.ToString() + GetPlatformSuffix();
			DeviceIdentifier = SystemInfo.deviceUniqueIdentifier;
			PersistentDataPath = Application.persistentDataPath;
			StreamingAssetsPath = Application.streamingAssetsPath;

			var info = SystemInformationExtractor.Instance.GetSystemInfo();
			DeviceManufacturer = info.DeviceManufacturer;
			OperatingSystemFamily = info.OperatingSystemFamily;
			OperatingSystemVersion = info.OperatingSystemVersion;
		}

		private static string GetPlatformSuffix()
		{
#if !UNITY_EDITOR
#if AMAZON_STORE
			return "Amazon";
#elif RUSTORE
			return "RuStore";
#elif NUTAKU
			return "Nutaku";
#elif HUAWEI
			return "Huawei";
#elif YANDEX
			return "Yandex";
//#elif CHINA
//			return "China";
#elif STEAM || MINIIT_STEAM || UNITY_STEAM
			return "Steam";
#endif
#endif
			return string.Empty;
		}
	}
}
