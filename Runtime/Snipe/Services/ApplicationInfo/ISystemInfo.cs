namespace MiniIT.Snipe
{
	public interface ISystemInfo
	{
		string DeviceManufacturer { get; }
		string OperatingSystemFamily { get; }
		Version OperatingSystemVersion { get; }
	}
}
