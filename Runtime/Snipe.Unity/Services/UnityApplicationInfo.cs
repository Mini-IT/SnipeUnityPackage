#if UNITY_WSA && !UNITY_EDITOR
#define SYSTEM_INFO
#endif
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public class UnityApplicationInfo : IApplicationInfo
#if SYSTEM_INFO
		, ISystemInfo
#endif
	{
		public string ApplicationIdentifier { get; }
		public string ApplicationVersion { get; }
		public string ApplicationPlatform { get; }
		public string DeviceIdentifier { get; }
		public string PersistentDataPath { get; }
		public string StreamingAssetsPath { get; }

#if SYSTEM_INFO
		public string DeviceManufacturer { get; }
		public string OperatingSystemFamily { get; }
		public Version OperatingSystemVersion { get; }
#endif

		public UnityApplicationInfo()
		{
			ApplicationIdentifier = Application.identifier;
			ApplicationVersion = Application.version;
			ApplicationPlatform = Application.platform.ToString() + GetPlatformSuffix();
			DeviceIdentifier = SystemInfo.deviceUniqueIdentifier;
			PersistentDataPath = Application.persistentDataPath;
			StreamingAssetsPath = Application.streamingAssetsPath;

#if SYSTEM_INFO
			var info = SystemInformationExtractor.GetSystemInfo();
			DeviceManufacturer = info.DeviceManufacturer;
			OperatingSystemFamily = info.OperatingSystemFamily;
			OperatingSystemVersion = info.OperatingSystemVersion;
#endif
		}

		private static string GetPlatformSuffix()
		{
			string suffix = string.Empty;

#if !UNITY_EDITOR
#if AMAZON_STORE
			suffix = "Amazon";
#elif RUSTORE
			suffix = "RuStore";
#elif NUTAKU
			suffix = "Nutaku";
#elif HUAWEI
			suffix = "Huawei";
#elif YANDEX
			suffix = "Yandex";
//#elif CHINA
//			suffix = "China";
#elif STEAM || MINIIT_STEAM || UNITY_STEAM
			suffix = "Steam";
#endif
#endif
			return suffix;
		}
	}
}
