using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Cysharp.Threading.Tasks;
using fastJSON;
using UnityEngine;

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

#if UNITY_EDITOR
			Debug.Log($"Load config with params: {requestParamsJson}");
#endif

			HttpWebResponse response = null;
			Dictionary<string, object> config = null;

			try
			{
				var request = WebRequest.CreateHttp(_url);
				request.Method = "POST";
				var content = await request.GetRequestStreamAsync();
				content.Write(Encoding.UTF8.GetBytes(requestParamsJson));
				var loadTask = request.GetResponseAsync();
				await loadTask;

				if (loadTask.IsFaulted || loadTask.IsCanceled)
				{
					Debug.LogError($"[{nameof(SnipeConfigLoader)}] loader failed");
					return config;
				}

				response = (HttpWebResponse)loadTask.Result;
				if (response == null)
				{
					Debug.Log($"[{nameof(SnipeConfigLoader)}] loader failed - http response is null");
					return config;
				}
				if (!new HttpResponseMessage(response.StatusCode).IsSuccessStatusCode)
				{
					Debug.Log($"[{nameof(SnipeConfigLoader)}] loader failed - {(int)response.StatusCode} {response.StatusCode}");
					return config;
				}

				using (var reader = new StreamReader(response.GetResponseStream()))
				{
					string responseMessage = await reader.ReadToEndAsync();
					Debug.Log($"[{nameof(SnipeConfigLoader)}] loader response: {responseMessage}");

					var fullResponse = (Dictionary<string, object>)JSON.Parse(responseMessage);
					if (fullResponse != null && fullResponse.TryGetValue("data", out var responseData))
					{
						config = (Dictionary<string, object>)responseData;
					}
				}

			}
			catch (Exception e)
			{
				Debug.LogError($"[{nameof(SnipeConfigLoader)}] loader failed: {e}");
			}
			finally
			{
				response?.Dispose();
			}

			return config;
		}
	}
}
