using System;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class DeviceIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
				{
					Value = SystemInfo.deviceUniqueIdentifier;
				}
				//else
				//{
					// TODO: generate device id using custom algorithm
				//}
			}
			callback?.Invoke(Value);
		}
	}
}