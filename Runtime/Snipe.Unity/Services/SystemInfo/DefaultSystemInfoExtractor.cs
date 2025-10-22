namespace MiniIT.Snipe
{
	public class DefaultSystemInfoExtractor : ISystemInformationExtractor
	{
		public SystemInformation GetSystemInfo()
		{
			return new SystemInformation()
			{
				DeviceManufacturer = UnityEngine.SystemInfo.deviceModel,
				OperatingSystemFamily = UnityEngine.SystemInfo.operatingSystemFamily.ToString(),
				OperatingSystemVersion = UnityEngine.SystemInfo.operatingSystem,
			};
		}
	}
}
