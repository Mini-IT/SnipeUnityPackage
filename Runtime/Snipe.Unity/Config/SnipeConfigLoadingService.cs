using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using fastJSON;
using UnityEngine;

namespace MiniIT.Snipe
{
	public interface ISnipeConfigLoadingService
	{
		Dictionary<string, object> Config { get; }
		UniTask<Dictionary<string, object>> Load(CancellationToken cancellationToken = default);
		void Reset();
	}

	public class SnipeConfigLoadingService : ISnipeConfigLoadingService
	{
		public Dictionary<string, object> Config => _config;

		private Dictionary<string, object> _config;
		private bool _loading = false;

		private SnipeConfigLoader _loader;
		private readonly string _projectID;

		public SnipeConfigLoadingService(string projectID)
		{
			_projectID = projectID;
		}

		public async UniTask<Dictionary<string, object>> Load(CancellationToken cancellationToken = default)
		{
			if (_config != null)
			{
				return _config;
			}

			if (_loading)
			{
				await UniTask.WaitWhile(() => _config == null, PlayerLoopTiming.Update, cancellationToken);
				return _config;
			}

			_loading = true;

			if (_loader == null)
			{
				await UniTask.WaitUntil(() => SnipeServices.IsInitialized, PlayerLoopTiming.Update, cancellationToken)
					.SuppressCancellationThrow();

				if (cancellationToken.IsCancellationRequested)
				{
					return _config;
				}

				_loader ??= new SnipeConfigLoader(_projectID, SnipeServices.ApplicationInfo);
			}

			var loadedAdditionalParams = await LoadAdditionalParamsAsync();

			string requestParams = BuildRequestParamsJson(SnipeServices.ApplicationInfo, loadedAdditionalParams);
			_loader.SetRequestParams(requestParams);

			_config = await _loader.Load();
			_loading = false;

			return _config;
		}

		public void Reset()
		{
			_config = null;
			_loading = false;
		}

		private string BuildRequestParamsJson(IApplicationInfo appInfo, Dictionary<string, object> additionalParams = null)
		{
			var requestParams = new Dictionary<string, object>
			{
				{ "project", _projectID },
				{ "deviceID", appInfo.DeviceIdentifier },
				{ "identifier", appInfo.ApplicationIdentifier },
				{ "version", appInfo.ApplicationVersion },
				{ "platform", appInfo.ApplicationPlatform },
				{ "packageVersion", PackageInfo.VERSION_CODE }
			};

			if (additionalParams != null)
			{
				foreach (var param in additionalParams)
				{
					requestParams[param.Key] = param.Value;
				}
			}

			return JSON.ToJSON(requestParams);
		}

		private async UniTask<Dictionary<string, object>> LoadAdditionalParamsAsync()
		{
			string filePath = Path.Combine(Application.persistentDataPath, string.Format("additionalParams{0}.json", Application.version));

			if (!File.Exists(filePath))
			{
				return new Dictionary<string, object>();
			}

			string json = await File.ReadAllTextAsync(filePath);
			return (Dictionary<string, object>)JSON.Parse(json);
		}
	}
}
