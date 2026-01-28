
using MiniIT.Snipe;

namespace MiniIT.Snipe.Unity
{
	public class DeviceIdBinding : AuthBinding<DeviceIdFetcher>
	{
		public DeviceIdBinding(ISnipeServices services)
			: base("dvid", services)
		{
#if UNITY_WEBGL
			UseContextIdPrefix = false;
#endif
		}
	}
}
