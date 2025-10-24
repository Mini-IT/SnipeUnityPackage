#if UNITY_IOS

namespace MiniIT.Snipe
{
	public class IosSystemInfoExtractor : ISystemInformationExtractor
	{
		public SystemInformation GetSystemInfo()
		{
			return new SystemInformation()
			{
				DeviceManufacturer = UnityEngine.SystemInfo.deviceModel,
				OperatingSystemFamily = UnityEngine.SystemInfo.operatingSystemFamily.ToString(),
				OperatingSystemVersion = UnityEngine.iOS.Device.systemVersion,
			};
		}
	}
}

#endif
