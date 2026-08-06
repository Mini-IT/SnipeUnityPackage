#if UNITY_WSA && !UNITY_EDITOR

namespace MiniIT.Snipe
{
	public class WindowsSystemInfoExtractor : ISystemInformationExtractor
	{
		public SystemInformation GetSystemInfo()
		{
			var eascdi = new Windows.Security.ExchangeActiveSyncProvisioning.EasClientDeviceInformation();

			string dfv = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamilyVersion;
			int major = 0;
			int minor = 0;
			if (ulong.TryParse(dfv, out ulong v))
			{
				major = (int)((v & 0xFFFF000000000000L) >> 48);
				minor = (int)((v & 0x0000FFFF00000000L) >> 32);
			}

			return new SystemInformation()
			{
				DeviceManufacturer = eascdi.SystemManufacturer,
				OperatingSystemFamily = eascdi.OperatingSystem,
				OperatingSystemVersion = $"{major}.{minor}",
			};
		}
	}
}

#endif
