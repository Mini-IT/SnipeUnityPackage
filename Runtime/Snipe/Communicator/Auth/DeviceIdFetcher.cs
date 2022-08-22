using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using MiniIT;

namespace MiniIT.Snipe
{
	public class DeviceIdFetcher : AuthIdFetcher
	{
		private static string mDeviceId = null;
		
		public override void Fetch(Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(mDeviceId))
			{
				if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
				{
					mDeviceId = SystemInfo.deviceUniqueIdentifier;
				}
				//else
				//{
					// TODO: generate device id using custom algorithm
				//}
			}
			callback?.Invoke(mDeviceId);
		}
	}
}