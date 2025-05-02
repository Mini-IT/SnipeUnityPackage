using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

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
		public SnipeConfigLoadingStatistics Statistics { get; } = new SnipeConfigLoadingStatistics();

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

			Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Initialization);

			if (_loader == null)
			{
				await UniTask.WaitUntil(() => SnipeServices.IsInitialized, PlayerLoopTiming.Update, cancellationToken)
					.SuppressCancellationThrow();

				if (cancellationToken.IsCancellationRequested)
				{
					Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Cancelled);
					TrackStats();
					return _config;
				}

				_loader ??= new SnipeConfigLoader(_projectID, SnipeServices.ApplicationInfo);
			}

			Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Loading);

			_config = await _loader.Load(Statistics);
			_loading = false;

			Statistics.Success = _config != null;
			Statistics.SetState(SnipeConfigLoadingStatistics.LoadingState.Finished);
			TrackStats();

			return _config;
		}

		private void TrackStats()
		{
			if (SnipeServices.Analytics.GetTracker() is ISnipeConfigLoadingAnalyticsTracker tracker)
			{
				tracker.TrackSnipeConfigLoadingStats(Statistics);
			}
		}
	}
}
