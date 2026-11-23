using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using fastJSON;
using Microsoft.Extensions.Logging;
using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;

namespace MiniIT.Snipe
{
	public class SnipeConfigLoader
	{
		private readonly string _projectID;
		private readonly string _url;
		private readonly IApplicationInfo _appInfo;
		private readonly ILogger _logger;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ISharedPrefs _sharedPrefs;

		public SnipeConfigLoader(string projectID, IApplicationInfo appInfo, ILogger logger, IHttpClientFactory httpClientFactory)
			: this(projectID, appInfo, logger, httpClientFactory, SharedPrefs.Instance)
		{
		}

		public SnipeConfigLoader(string projectID, IApplicationInfo appInfo, ILogger logger, IHttpClientFactory httpClientFactory, ISharedPrefs sharedPrefs)
		{
			_projectID = projectID;
			_appInfo = appInfo;
			_url = "https://config.snipe.dev/api/v2/configStrings";
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
			_sharedPrefs = sharedPrefs ?? throw new ArgumentNullException(nameof(sharedPrefs));
		}

		public async UniTask<Dictionary<string, object>> Load(TimeSpan timeout, Dictionary<string, object> additionalParams = null,
			SnipeConfigLoadingStatistics loadingStatistics = null, CancellationToken cancellationToken = default)
		{
			IHttpClient httpClient = _httpClientFactory.CreateHttpClient();

			if (loadingStatistics != null)
			{
				loadingStatistics.ClientImplementation = httpClient.GetType().Name;
			}

			try
			{
				await UniTask.SwitchToMainThread(cancellationToken);
				var requestParams = BuildRequestParams(additionalParams);
				string prefsKey = $"SnipeConfig.AbTests.{requestParams["project"]}";
				requestParams["previousAbTests"] = JSON.Parse(_sharedPrefs.GetString(prefsKey, "[]")) as List<object> ?? new List<object>();
				string requestParamsJson = JSON.ToJSON(requestParams);

				var response = await httpClient.PostJson(new Uri(_url), requestParamsJson, timeout, cancellationToken);

				if (!response.IsSuccess)
				{
					_logger.LogTrace($"loader failed. Status Code: {response.ResponseCode}; Error Message: '{response.Error}'");
					return null;
				}

				string responseMessage = await response.GetStringContentAsync();
				_logger.LogTrace($"loader response: {responseMessage}");

				if (JSON.Parse(responseMessage) is not Dictionary<string, object> fullResponse ||
				    !fullResponse.TryGetValue("data", out var responseData) ||
				    responseData is not Dictionary<string, object> config)
				{
					return null;
				}

				// Inject AB-tests
				// "abTests":[{"id":1,"stringID":"testString","variantID":1}]
				var assignments = new List<Dictionary<string, object>>();
				if (fullResponse.TryGetValue("abTests", out var testsList) && testsList is List<object> tests)
				{
					foreach (var testData in tests)
					{
						if (testData is not IDictionary<string, object> test || !test.TryGetValue("variantID", out var testVariantID))
						{
							continue;
						}

						if (test.TryGetValue("stringID", out var testStringID))
						{
							config[$"test_{testStringID}"] = $"test_{testStringID}_Variant{testVariantID}";
						}

						if (test.TryGetValue("id", out var testID) && testID is long && testVariantID is long)
						{
							assignments.Add(new Dictionary<string, object> { ["id"] = testID, ["variantID"] = testVariantID });
						}
					}
				}

				await UniTask.SwitchToMainThread(cancellationToken);
				_sharedPrefs.SetString(prefsKey, JSON.ToJSON(assignments));
				_sharedPrefs.Save();
				return config;
			}
			catch (Exception e)
			{
				_logger.LogTrace($"loader failed: {LogUtil.GetReducedException(e)}");
				return null;
			}
			finally
			{
				if (httpClient is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
		}

		private Dictionary<string, object> BuildRequestParams(Dictionary<string, object> additionalParams)
		{
			var requestParams = new Dictionary<string, object>
			{
				["project"] = _projectID,
				["deviceID"] = _appInfo.DeviceIdentifier,
				["identifier"] = _appInfo.ApplicationIdentifier,
				["version"] = _appInfo.ApplicationVersion,
				["platform"] = _appInfo.ApplicationPlatform,
				["packageVersion"] = PackageInfo.VERSION_CODE,
				["deviceName"] = _appInfo.DeviceManufacturer,
				["osName"] = _appInfo.OperatingSystemFamily,
				["osVersion"] = _appInfo.OperatingSystemVersion,
			};

			if (additionalParams != null)
			{
				foreach (var param in additionalParams)
				{
					requestParams[param.Key] = param.Value;
				}
			}

			return requestParams;
		}
	}
}
