namespace MiniIT.Snipe
{
	public class DefaultSystemInfoExtractor : ISystemInformationExtractor
	{
		public SystemInformation GetSystemInfo()
		{
			var osFamily = UnityEngine.SystemInfo.operatingSystemFamily;
			string osVersion = UnityEngine.SystemInfo.operatingSystem;
			string osFamilyString = null;

			if (osFamily == UnityEngine.OperatingSystemFamily.Other)
			{
				string lowerVersion = osVersion.ToLowerInvariant();
				if (lowerVersion.Contains("android"))
				{
					osFamilyString = "Android";
					osVersion = osVersion.Replace("Android", "").Trim();
				}
				else if (lowerVersion.Contains("ios"))
				{
					osFamilyString = "iOS";
					osVersion = osVersion.Replace("iOS", "").Trim();
				}
			}

			return new SystemInformation()
			{
				DeviceManufacturer = UnityEngine.SystemInfo.deviceModel,
				OperatingSystemFamily = osFamilyString ?? osFamily.ToString(),
				OperatingSystemVersion = osVersion,
			};
		}
	}
}
