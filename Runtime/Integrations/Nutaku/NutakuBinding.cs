namespace MiniIT.Snipe.Unity
{
	public class NutakuBinding : AuthBinding<NutakuIdFetcher>
	{
		public NutakuBinding()
			: base("nuta")
		{
			UseContextIdPrefix = false;
		}
	}
}
