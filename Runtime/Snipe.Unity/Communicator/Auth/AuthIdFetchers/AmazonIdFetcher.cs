using System;
using Cysharp.Threading.Tasks;
using MiniIT.Threading;

namespace MiniIT.Snipe.Unity
{
	public class AmazonIdFetcher : AuthIdFetcher
	{
		public static AuthIdObserver Observer { get; } = new AuthIdObserver();

		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			//Debug.LogTrace($"[AmazonIdFetcher] Fetch. Value = {Value}");
			//if (string.IsNullOrEmpty(Value))
			//{
			//	using (var pluginClass = new AndroidJavaClass("com.unity.purchasing.amazon.AmazonPurchasing"))
			//	{
			//		Debug.LogTrace($"[AmazonIdFetcher] Fetch - pluginClass");

			//		//var proxy = new UnityEngine.Purchasing.JavaBridge(new ScriptingUnityCallback(callback, util));
			//		var plugin = pluginClass.CallStatic<AndroidJavaObject>("instance" /*, proxy*/);
			//		Debug.LogTrace($"[AmazonIdFetcher] Fetch - plugin = {plugin}");
			//		var value = plugin.Call<string>("getAmazonUserId");
			//		Debug.LogTrace($"[AmazonIdFetcher] Fetch - value = {value}");
			//		SetValue(value);
			//		Debug.LogTrace($"[AmazonIdFetcher] Fetch - Value = {Value}");
			//	}
			//}

			if (CheckValueValid(Observer.Value))
			{
				Value = Observer.Value;
			}

			if (wait_initialization && string.IsNullOrEmpty(Value))
			{
				WaitForInitialization(callback).Forget();
				return;
			}

			callback?.Invoke(Value);
		}

		private async UniTaskVoid WaitForInitialization(Action<string> callback)
		{
			while (string.IsNullOrEmpty(Value))
			{
				await AlterTask.Delay(100);

				if (CheckValueValid(Observer.Value))
				{
					Value = Observer.Value;
					break;
				}
			}

			callback?.Invoke(Value);
		}

		public void SetValue(string value)
		{
			Value = CheckValueValid(value) ? value : "";
		}

		private static bool CheckValueValid(string value)
		{
			return !string.IsNullOrEmpty(value) && value.ToLower() != "fakeid";
		}
	}
}
