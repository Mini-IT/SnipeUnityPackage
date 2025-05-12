using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using fastJSON;
using Microsoft.Extensions.Logging;
using MiniIT.Http;

namespace MiniIT.Snipe
{
	public class SnipeConfigLoader
	{
		private readonly string _projectID;
		private readonly string _url;
		private readonly IApplicationInfo _appInfo;
		private readonly ILogger _logger;

		public SnipeConfigLoader(string projectID, IApplicationInfo appInfo)
		{
			_projectID = projectID;
			_appInfo = appInfo;
			_url = "https://config.snipe.dev/api/v1/configStrings";
			_logger = SnipeServices.LogService.GetLogger<SnipeConfigLoader>();
		}

		public async UniTask<Dictionary<string, object>> Load(SnipeConfigLoadingStatistics loadingStatistics = null)
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

			IHttpClient httpClient = SnipeServices.HttpClientFactory.CreateHttpClient();

			if (loadingStatistics != null)
			{
				loadingStatistics.ClientImplementation = httpClient.GetType().Name;
			}

			try
			{
				var response = await httpClient.PostJson(new Uri(_url), requestParamsJson);

				if (!response.IsSuccess)
				{
					_logger.LogTrace($"loader failed. Status Code: {response.ResponseCode}; Error Message: '{response.Error}'");
					return null;
				}

				string responseMessage = await response.GetStringContentAsync();
				_logger.LogTrace($"loader response: {responseMessage}");

				var fullResponse = (Dictionary<string, object>)JSON.Parse(responseMessage);
				if (fullResponse != null)
				{
					if (fullResponse.TryGetValue("data", out var responseData))
					{
						config = (Dictionary<string, object>)responseData;
					}

					// Inject AB-tests
					// "abTests":[{"id":1,"stringID":"testString","variantID":1}]
					if (config != null &&
					    fullResponse.TryGetValue("abTests", out var testsList) &&
					    testsList is IEnumerable tests)
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
				_logger.LogTrace($"loader failed: {e}");
			}
			finally
			{
				if (httpClient is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}

			return config;
		}
	}
}
