#if NUTAKU
using System;
using Nutaku.Unity;

namespace MiniIT.Snipe.Unity
{
	public class NutakuIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				string userId = SdkPlugin.loginInfo?.userId;
				if (!string.IsNullOrEmpty(userId))
				{
					SetValue(userId);
				}
			}
			callback?.Invoke(Value);
		}

		public void SetValue(string value)
		{
			if (CheckValueValid(value))
				Value = value;
			else
				Value = "";
		}

		private static bool CheckValueValid(string value)
		{
			return !string.IsNullOrEmpty(value) && value.ToLower() != "fakeid";
		}
	}
}
#endif
