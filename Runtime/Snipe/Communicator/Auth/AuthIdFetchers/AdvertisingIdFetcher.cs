using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class AdvertisingIdFetcher : AuthIdFetcher
	{
		private TaskScheduler _mainThreadScheduler;
		
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (!string.IsNullOrEmpty(Value))
			{
				callback?.Invoke(Value);
				return;
			}
			
			_mainThreadScheduler = (SynchronizationContext.Current != null) ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;
			
#if MINI_IT_ADVERTISING_ID
			MiniIT.Utils.AdvertisingIdFetcher.RequestAdvertisingId((advertising_id) =>
			{
				SetAdvertisingId(advertising_id);
				
				if (callback != null)
				{
					RunInMainThread(() => callback.Invoke(Value));
				}
			});
#else
			if (!Application.RequestAdvertisingIdentifierAsync((advertising_id, tracking_enabled, error) =>
				{
					SetAdvertisingId(advertising_id);
					callback?.Invoke(Value);
				}))
			{
#if UNITY_IOS
				if (wait_initialization && string.IsNullOrEmpty(Value))
				{
					if (callback != null)
					{
						Task.Run(() => WaitForInitialization(callback));
					}
					return;
				}
#endif
				callback?.Invoke(null);
			}
#endif
		}

#if UNITY_IOS
		private async Task WaitForInitialization(Action<string> callback)
		{
			while (string.IsNullOrEmpty(Value))
			{
				await Task.Delay(100);
			}
			RunInMainThread(() => callback.Invoke(Value));
		}
#endif

		private void SetAdvertisingId(string advertising_id)
		{
			if (CheckAdvertisingIdValid(advertising_id))
				Value = advertising_id;
			else
				Value = "";
		}
		
		private static bool CheckAdvertisingIdValid(string advertising_id)
		{
			if (string.IsNullOrEmpty(advertising_id))
				return false;

			// on IOS value may be "00000000-0000-0000-0000-000000000000"
			return Regex.IsMatch(advertising_id, @"[^0\W]");
		}
		
		private void RunInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}
	}
}