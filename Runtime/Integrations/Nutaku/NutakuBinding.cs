namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuBinding : AuthBinding<NutakuIdFetcher>
	{
		public NutakuBinding()
			: base("nuta")
		{
			AvailableForRegistration = true;
			UseContextIdPrefix = false;
		}
	}
}
