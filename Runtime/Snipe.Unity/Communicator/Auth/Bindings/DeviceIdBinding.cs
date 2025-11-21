
namespace MiniIT.Snipe.Unity
{
	public class DeviceIdBinding : AuthBinding<DeviceIdFetcher>
	{
		public DeviceIdBinding()
			: base("dvid")
		{
#if UNITY_WEBGL
			UseContextIdPrefix = false;
#endif
		}
	}
}
