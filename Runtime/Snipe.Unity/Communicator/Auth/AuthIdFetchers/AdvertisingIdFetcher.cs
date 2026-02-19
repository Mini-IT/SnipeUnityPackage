using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;

#if !MINI_IT_ADVERTISING_ID
using UnityEngine;
#endif

namespace MiniIT.Snipe.Unity
{
	public class AdvertisingIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool waitInitialization, Action<string> callback = null)
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
				if (waitInitialization && string.IsNullOrEmpty(Value))
				{
					if (callback != null)
					{
						WaitForInitialization(callback).Forget();
					}
					return;
				}
#endif
				callback?.Invoke(null);
			}
#endif
		}

#if UNITY_IOS
		private async UniTaskVoid WaitForInitialization(Action<string> callback)
		{
			while (string.IsNullOrEmpty(Value))
			{
				try
				{
					await UniTask.Delay(100, cancellationToken: _cts.Token);
				}
				catch (OperationCanceledException)
				{
					return;
				}
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
			if (Services == null)
			{
				throw new InvalidOperationException("Services not initialized.");
			}

			Services.MainThreadRunner.RunInMainThread(action);
		}
	}
}
