﻿using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MiniIT.Threading;
#if !MINI_IT_ADVERTISING_ID
using UnityEngine;
#endif

namespace MiniIT.Snipe.Unity
{
	public class AdvertisingIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
#if UNITY_WEBGL
			callback?.Invoke(null);
			return;
#endif

			if (!string.IsNullOrEmpty(Value))
			{
				callback?.Invoke(Value);
				return;
			}

#if MINI_IT_ADVERTISING_ID
			MiniIT.Utils.AdvertisingIdFetcher.RequestAdvertisingId((advertisingId, trackingEnabled, errorMessage) =>
			{
				SetAdvertisingId(advertisingId);
				
				if (callback != null)
				{
					RunInMainThread(() => callback.Invoke(Value));
				}
			});
#else
			if (!Application.RequestAdvertisingIdentifierAsync((advertisingId, trackingEnabled, errorMessage) =>
				{
					SetAdvertisingId(advertisingId);
					callback?.Invoke(Value);
				}))
			{
#if UNITY_IOS
				if (wait_initialization && string.IsNullOrEmpty(Value))
				{
					if (callback != null)
					{
						AlterTask.Run(() => WaitForInitialization(callback));
					}
					return;
				}
#endif
				callback?.Invoke(null);
			}
#endif
		}

#if UNITY_IOS
		private async UniTask WaitForInitialization(Action<string> callback)
		{
			while (string.IsNullOrEmpty(Value))
			{
				await TaskHelper.Delay(100);
			}
			RunInMainThread(() => callback.Invoke(Value));
		}
#endif

		private void SetAdvertisingId(string advertisingId)
		{
			if (CheckAdvertisingIdValid(advertisingId))
				Value = advertisingId;
			else
				Value = "";
		}
		
		private static bool CheckAdvertisingIdValid(string advertisingId)
		{
			if (string.IsNullOrEmpty(advertisingId))
				return false;

			// on IOS value may be "00000000-0000-0000-0000-000000000000"
			return Regex.IsMatch(advertisingId, @"[^0\W]");
		}
		
		private void RunInMainThread(Action action)
		{
			SnipeServices.MainThreadRunner.RunInMainThread(action);
		}
	}
}
