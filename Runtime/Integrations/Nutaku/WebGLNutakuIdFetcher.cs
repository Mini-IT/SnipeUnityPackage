using System;

namespace MiniIT.Snipe.Unity
{
	public sealed class WebGLNutakuIdFetcher : AbstractNutakuIdFetcher
	{
		protected override void InternalFetch(bool waitInitialization, Action<string> callback = null)
		{
			if (waitInitialization && string.IsNullOrEmpty(Value))
			{
				WaitForInitialization().Forget();
				return;
			}
			SetValuesFromMono();
		}
	}
}
