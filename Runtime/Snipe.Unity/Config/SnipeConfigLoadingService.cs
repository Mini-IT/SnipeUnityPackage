using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe
{
	public interface ISnipeConfigLoadingService
	{
		Dictionary<string, object> Config { get; }
		UniTask<Dictionary<string, object>> Load(Dictionary<string, object> additionalRequestParams = null, CancellationToken cancellationToken = default);
		void Reset();
	}

	public class SnipeConfigLoadingService : ISnipeConfigLoadingService, IDisposable
	{
		public Dictionary<string, object> Config => _config;

		private Dictionary<string, object> _config;
		private bool Loading => _loadingCancellation != default;

		private SnipeConfigLoader _loader;
		private readonly string _projectID;

		private CancellationTokenSource _loadingCancellation;

		public SnipeConfigLoadingService(string projectID)
		{
			_projectID = projectID;
		}

		public void Dispose()
		{
			if (_loadingCancellation != null)
			{
				_loadingCancellation.Dispose();
				_loadingCancellation = null;
			}
		}

		public async UniTask<Dictionary<string, object>> Load(Dictionary<string, object> additionalRequestParams = null, CancellationToken cancellationToken = default)
		{
			if (_config != null)
			{
				return _config;
			}

			if (Loading)
			{
				await UniTask.WaitWhile(() => _config == null, PlayerLoopTiming.Update, cancellationToken);
				return _config;
			}

			if (_loadingCancellation != null)
			{
				_loadingCancellation.Cancel();
				_loadingCancellation.Dispose();
			}

			_loadingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var loadingToken = _loadingCancellation.Token;

			if (_loader == null)
			{
				await UniTask.WaitUntil(() => SnipeServices.IsInitialized, PlayerLoopTiming.Update, loadingToken)
					.SuppressCancellationThrow();

				if (loadingToken.IsCancellationRequested)
				{
					return _config;
				}

				_loader ??= new SnipeConfigLoader(_projectID, SnipeServices.ApplicationInfo);
			}

			_config = await _loader.Load(additionalRequestParams);

			_loadingCancellation?.Dispose();
			_loadingCancellation = null;

			return _config;
		}

		public void Reset()
		{
			if (_loadingCancellation != null)
			{
				_loadingCancellation.Cancel();
				_loadingCancellation.Dispose();
				_loadingCancellation = null;
			}

			_config = null;
		}
	}
}
