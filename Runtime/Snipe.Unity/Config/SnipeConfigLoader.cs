using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using fastJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace MiniIT.Snipe
{
	public class SnipeConfigLoader
	{
		private readonly string _projectID;
		private readonly string _url;
		private readonly IApplicationInfo _appInfo;

		public SnipeConfigLoader(string projectID, IApplicationInfo appInfo)
		{
			_projectID = projectID;
			_appInfo = appInfo;
			_url = "https://config.snipe.dev/api/v1/configStrings";
		}

		public async UniTask<Dictionary<string, object>> Load()
		{
			string requestParamsJson = "{" +
				$"\"project\":\"{_projectID}\"," +
				$"\"deviceID\":\"{_appInfo.DeviceIdentifier}\"," +
				$"\"identifier\":\"{_appInfo.ApplicationIdentifier}\"," +
				$"\"version\":\"{_appInfo.ApplicationVersion}\"," +
				$"\"platform\":\"{_appInfo.ApplicationPlatform}\"," +
				$"\"packageVersion\":\"{PackageInfo.VERSION_CODE}\"" +
				"}";

			Dictionary<string, object> config = null;

			try
			{
				using var request = UnityWebRequest.Post(_url, requestParamsJson, "application/json");
				request.downloadHandler = new DownloadHandlerBuffer();
				var response = await request.SendWebRequest().ToUniTask();

				if (response.result != UnityWebRequest.Result.Success)
				{
					Debug.Log($"[{nameof(SnipeConfigLoader)}] loader failed. {response.error}");
					return config;
				}

				string responseMessage = response.downloadHandler.text;
				Debug.Log($"[{nameof(SnipeConfigLoader)}] loader response: {responseMessage}");

				var fullResponse = (Dictionary<string, object>)JSON.Parse(responseMessage);
				if (fullResponse != null)
				{
					if (fullResponse.TryGetValue("data", out var responseData))
					{
						config = (Dictionary<string, object>)responseData;
					}

					// Inject AB-tests
					// "abTests":[{"id":1,"stringID":"testString","variantID":1}]
					if (fullResponse.TryGetValue("abTests", out var testsList) && testsList is IEnumerable tests)
					{
						foreach (var testData in tests)
						{
							if (testData is IDictionary<string, object> test &&
								test.TryGetValue("stringID", out var testStringID) &&
								test.TryGetValue("variantID", out var testVariantID))
							{
								config[$"test_{testStringID}"] = $"test_{testStringID}_Variant{testVariantID}";
							}
						}
					}
				}

			}
			catch (Exception e)
			{
				Debug.Log($"[{nameof(SnipeConfigLoader)}] loader failed: {e}");
			}

			return config;
		}
	}
}
