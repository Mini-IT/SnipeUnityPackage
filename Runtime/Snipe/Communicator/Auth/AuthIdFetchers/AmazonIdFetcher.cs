﻿using System;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public class AmazonIdFetcher : AuthIdFetcher
	{
		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			//Debug.Log($"[AmazonIdFetcher] Fetch. Value = {Value}");
			//if (string.IsNullOrEmpty(Value))
			//{
			//	using (var pluginClass = new AndroidJavaClass("com.unity.purchasing.amazon.AmazonPurchasing"))
			//	{
			//		Debug.Log($"[AmazonIdFetcher] Fetch - pluginClass");

			//		//var proxy = new UnityEngine.Purchasing.JavaBridge(new ScriptingUnityCallback(callback, util));
			//		var plugin = pluginClass.CallStatic<AndroidJavaObject>("instance" /*, proxy*/);
			//		Debug.Log($"[AmazonIdFetcher] Fetch - plugin = {plugin}");
			//		var value = plugin.Call<string>("getAmazonUserId");
			//		Debug.Log($"[AmazonIdFetcher] Fetch - value = {value}");
			//		SetValue(value);
			//		Debug.Log($"[AmazonIdFetcher] Fetch - Value = {Value}");
			//	}
			//}

			if (wait_initialization && string.IsNullOrEmpty(Value))
			{
				Task.Run(() => WaitForInitialization(callback));
				return;
			}

			callback?.Invoke(Value);
		}

		private async Task WaitForInitialization(Action<string> callback)
		{
			while (string.IsNullOrEmpty(Value))
			{
				await Task.Delay(100);
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