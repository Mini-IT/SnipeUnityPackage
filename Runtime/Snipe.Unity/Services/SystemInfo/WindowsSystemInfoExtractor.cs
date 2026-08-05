#if UNITY_WSA && !UNITY_EDITOR

namespace MiniIT.Snipe
{
	public class WindowsSystemInfoExtractor : ISystemInformationExtractor
	{
		public SystemInformation GetSystemInfo()
		{
			var eascdi = new Windows.Security.ExchangeActiveSyncProvisioning.EasClientDeviceInformation();

			string dfv = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamilyVersion;
			string osVersion = "0.0";
			if (ulong.TryParse(dfv, out ulong v))
			{
				var major = (int)((v & 0xFFFF000000000000L) >> 48);
				var minor = (int)((v & 0x0000FFFF00000000L) >> 32);
				osVersion = $"{major}.{minor}";
			}

			return new SystemInformation()
			{
				DeviceManufacturer = eascdi.SystemManufacturer,
				OperatingSystemFamily = eascdi.OperatingSystem,
				OperatingSystemVersion = osVersion,
			};
		}
	}
}

#endif
