namespace MiniIT.Snipe
{
	public interface IApplicationInfo
	{
		string ApplicationIdentifier { get; }
		string ApplicationVersion { get; }
		string ApplicationPlatform { get; }
		string DeviceIdentifier { get; }
		string PersistentDataPath { get; }
		string StreamingAssetsPath { get; }
	}
}
