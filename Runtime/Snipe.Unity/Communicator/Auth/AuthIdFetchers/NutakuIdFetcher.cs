#if NUTAKU

using System;
using NutakuUnitySdk;

namespace MiniIT.Snipe.Unity
{
	public class NutakuIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				string userId = NutakuCurrentUser.GetUserId().ToString();
				SetValue(userId);
			}
			callback?.Invoke(Value);
		}

		private void SetValue(string value)
		{
			Value = CheckValueValid(value) ? value : "";
		}

		private static bool CheckValueValid(string value)
		{
			return !string.IsNullOrEmpty(value);
		}
	}
}

#endif
