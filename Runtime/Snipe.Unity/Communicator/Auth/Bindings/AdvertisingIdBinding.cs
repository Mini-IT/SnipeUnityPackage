
namespace MiniIT.Snipe.Unity
{
	public class AdvertisingIdBinding : AuthBinding<AdvertisingIdFetcher>
	{
		public AdvertisingIdBinding(ISnipeServices services)
			: base("adid", services)
		{
			AvailableForRegistration = true;
		}
	}
}
