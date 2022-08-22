using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using MiniIT;

namespace MiniIT.Snipe
{
	public class AdvertisingIdFetcher : AuthIdFetcher
	{
		private static string mAdvertisingId = null;
		
		public override void Fetch(Action<string> callback = null)
		{
			if (!string.IsNullOrEmpty(mAdvertisingId))
			{
				callback?.Invoke(mAdvertisingId);
				return;
			}
			
#if MINI_IT_ADVERTISING_ID
			MiniIT.Utils.AdvertisingIdFetcher.RequestAdvertisingId((advertising_id) =>
			{
				SetAdvertisingId(advertising_id);
				callback?.Invoke(mAdvertisingId);
			});
#else
			if (!Application.RequestAdvertisingIdentifierAsync((advertising_id, tracking_enabled, error) =>
				{
					mAdvertisingId = advertising_id;
					callback?.Invoke(mAdvertisingId);
				}))
			{
				callback?.Invoke(null);
			}
#endif
		}
		
		private static void SetAdvertisingId(string advertising_id)
		{
			if (CheckAdvertisingIdValid(advertising_id))
				mAdvertisingId = advertising_id;
			else
				mAdvertisingId = "";
		}
		
		private static bool CheckAdvertisingIdValid(string advertising_id)
		{
			if (string.IsNullOrEmpty(advertising_id))
				return false;

			// on IOS value may be "00000000-0000-0000-0000-000000000000"
			return Regex.IsMatch(advertising_id, @"[^0\W]");
		}
	}
}