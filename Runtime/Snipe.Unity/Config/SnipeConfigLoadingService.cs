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
		public SnipeConfigLoadingStatistics Statistics { get; } = new SnipeConfigLoadingStatistics();

		private Dictionary<string, object> _config;
		private bool Loading => _loadingCancellation != null;

		private SnipeConfigLoader _loader;
		private readonly string _projectID;

		private readonly object _statisticsLock = new object();
		private bool _statisticsSent = false;
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
				await UniTask.WaitWhile(() => _config == null, cancellationToken: cancellationToken)
					.SuppressCancellationThrow();

				return _config;
			}

			lock (_statisticsLock)
			{
				Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Initialization);
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
				bool cancelled = await UniTask.WaitUntil(() => SnipeServices.IsInitialized, cancellationToken: loadingToken)
					.SuppressCancellationThrow();

				if (cancelled || loadingToken.IsCancellationRequested)
				{
					lock (_statisticsLock)
					{
						Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Cancelled);
					}

					TrackStats();

					return _config;
				}

				_loader ??= new SnipeConfigLoader(_projectID, SnipeServices.Instance.ApplicationInfo);
			}

			lock (_statisticsLock)
			{
				Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Loading);
			}

			_config = await _loader.Load(additionalRequestParams, Statistics);

			lock (_statisticsLock)
			{
				Statistics.Success = _config != null;
				Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Finished);
			}

			TrackStats();

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

		private void TrackStats()
		{
			if (_statisticsSent)
			{
				return;
			}

			_statisticsSent = true;

			if (SnipeServices.Instance.Analytics.GetTracker() is ISnipeConfigLoadingAnalyticsTracker tracker)
			{
				tracker.TrackSnipeConfigLoadingStats(Statistics);
			}
		}
	}
}
