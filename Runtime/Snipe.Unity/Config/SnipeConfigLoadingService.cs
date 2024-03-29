using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public interface ISnipeConfigLoadingService
	{
		Dictionary<string, object> Config { get; }
		UniTask<Dictionary<string, object>> Load(CancellationToken cancellationToken = default);
	}

	public class SnipeConfigLoadingService : ISnipeConfigLoadingService
	{
		public Dictionary<string, object> Config => _config;

		private Dictionary<string, object> _config;
		private bool _loading = false;

		private SnipeConfigLoader _loader;
		private readonly string _projectID;
		private readonly SnipeConfigFile _configFile;

		public SnipeConfigLoadingService(string projectID)
		{
			_configFile = new SnipeConfigFile();
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

			var localConfig = new Dictionary<string, object>();
			await _configFile.LoadAndMerge(localConfig);

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

			var remoteConfig = await _loader.Load();

			if (remoteConfig != null)
			{
				DictionaryUtility.Merge(localConfig, remoteConfig);
				_configFile.SaveConfig(localConfig);
			}

			_config = localConfig;
			return _config;
		}
	}
}
