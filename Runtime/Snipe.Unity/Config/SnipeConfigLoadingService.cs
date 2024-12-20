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
		UniTask<Dictionary<string, object>> Load();
		void Reset();
	}

	public class SnipeConfigLoadingService : ISnipeConfigLoadingService
	{
		public Dictionary<string, object> Config => _config;

		private Dictionary<string, object> _config;
		private bool _loading = false;

		private SnipeConfigLoader _loader;
		private readonly string _projectID;

		private CancellationTokenSource _loadingCancellation;

		public SnipeConfigLoadingService(string projectID)
		{
			_projectID = projectID;
		}

		public async UniTask<Dictionary<string, object>> Load()
		{
			if (_config != null)
			{
				return _config;
			}

			if (_loading)
			{
				await UniTask.WaitWhile(() => _config == null, PlayerLoopTiming.Update, _loadingCancellation.Token);
				return _config;
			}

			_loadingCancellation ??= new CancellationTokenSource();
			_loading = true;

			if (_loader == null)
			{
				await UniTask.WaitUntil(() => SnipeServices.IsInitialized, PlayerLoopTiming.Update, _loadingCancellation.Token)
					.SuppressCancellationThrow();

				if (_loadingCancellation.Token.IsCancellationRequested)
				{
					return _config;
				}

				_loader ??= new SnipeConfigLoader(_projectID, SnipeServices.ApplicationInfo);
			}

			var loadedAdditionalParams = await LoadAdditionalParamsAsync();

			_config = await _loader.Load(loadedAdditionalParams);
			_loading = false;

			return _config;
		}

		public void Reset()
		{
			_loadingCancellation.Cancel();
			_loadingCancellation.Dispose();
			_loadingCancellation = null;
			_config = null;
			_loading = false;
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
