#if UNITY_WSA && !UNITY_EDITOR

namespace MiniIT.Snipe
{
	public class WindowsSystemInfoExtractor : ISystemInformationExtractor
	{
		public SystemInformation GetSystemInfo()
		{
			var eascdi = new Windows.Security.ExchangeActiveSyncProvisioning.EasClientDeviceInformation();

			string dfv = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamilyVersion;
			var osVersion = new Version();
			if (ulong.TryParse(dfv, out ulong v))
			{
				osVersion.Major = (int)((v & 0xFFFF000000000000L) >> 48);
				osVersion.Minor = (int)((v & 0x0000FFFF00000000L) >> 32);
				// osVersion.Build = (int)((v & 0x00000000FFFF0000L) >> 16);
				// osVersion.Revision = (int)(v & 0x000000000000FFFFL);
			}

			return new SystemInformation()
			{
				DeviceManufacturer = eascdi.SystemManufacturer,
				OperatingSystemFamily = eascdi.OperatingSystem,
				OperatingSystemVersion = $"{osVersion.Major}.{osVersion.Minor}",
			};
		}
	}
}

#endif
