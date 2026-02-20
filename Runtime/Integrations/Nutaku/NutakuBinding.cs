namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuBinding : AuthBinding<NutakuIdFetcher>
	{
		public NutakuBinding(ISnipeServices services)
			: base("nuta", services)
		{
			AvailableForRegistration = true;
			UseContextIdPrefix = false;
		}
	}
}
