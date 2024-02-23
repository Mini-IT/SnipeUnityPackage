
namespace MiniIT.Snipe
{
	public static class SystemInformationExtractor
	{
		public struct SystemInfo
		{
			public string DeviceManufacturer;
			public string OperatingSystemFamily;
			public Version OperatingSystemVersion;
		}

		public struct Version
		{
			public int Major;
			public int Minor;
			public int Build;
			public int Revision;
		}

		public static SystemInfo GetSystemInfo()
		{
			var info = new SystemInfo();

#if UNITY_WSA && !UNITY_EDITOR
		var eascdi = new Windows.Security.ExchangeActiveSyncProvisioning.EasClientDeviceInformation();
		info.DeviceManufacturer = eascdi.SystemManufacturer;
		info.OperatingSystemFamily = eascdi.OperatingSystem;

		string dfv = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamilyVersion;
		if (ulong.TryParse(dfv, out ulong v))
		{
			info.OperatingSystemVersion.Major = (int)((v & 0xFFFF000000000000L) >> 48);
			info.OperatingSystemVersion.Minor = (int)((v & 0x0000FFFF00000000L) >> 32);
			info.OperatingSystemVersion.Build = (int)((v & 0x00000000FFFF0000L) >> 16);
			info.OperatingSystemVersion.Revision = (int)(v & 0x000000000000FFFFL);
		}
#endif

			return info;
		}
	}
}