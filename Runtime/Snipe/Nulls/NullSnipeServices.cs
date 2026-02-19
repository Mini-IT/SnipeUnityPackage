using System;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniIT.Http;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class NullSnipeServices : ISnipeServices
	{
		public ISharedPrefs SharedPrefs { get; }
		public ILoggerFactory LoggerFactory { get; }
		public ISnipeAnalyticsService Analytics { get; }
		public IMainThreadRunner MainThreadRunner { get; }
		public IApplicationInfo ApplicationInfo { get; }
		public IStopwatchFactory FuzzyStopwatchFactory { get; }
		public IHttpClientFactory HttpClientFactory { get; }
		public IInternetReachability InternetReachability { get; }
		public ITicker Ticker { get; }

		public NullSnipeServices()
			: this(new NullSharedPrefs(),
				NullLoggerFactory.Instance,
				new NullSnipeAnalyticsService(),
				new ImmediateMainThreadRunner(),
				new NullApplicationInfo(),
				new NullStopwatchFactory(),
				new NullHttpClientFactory(),
				new NullInternetReachability(),
				new NullTicker())
		{
		}

		public NullSnipeServices(
			ISharedPrefs sharedPrefs,
			ILoggerFactory loggerFactory,
			ISnipeAnalyticsService analytics,
			IMainThreadRunner mainThreadRunner,
			IApplicationInfo applicationInfo,
			IStopwatchFactory fuzzyStopwatchFactory,
			IHttpClientFactory httpClientFactory,
			IInternetReachability internetReachability,
			ITicker ticker)
		{
			SharedPrefs = sharedPrefs ?? throw new ArgumentNullException(nameof(sharedPrefs));
			LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			Analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
			MainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
			ApplicationInfo = applicationInfo ?? throw new ArgumentNullException(nameof(applicationInfo));
			FuzzyStopwatchFactory = fuzzyStopwatchFactory ?? throw new ArgumentNullException(nameof(fuzzyStopwatchFactory));
			HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
			InternetReachability = internetReachability ?? throw new ArgumentNullException(nameof(internetReachability));
			Ticker = ticker ?? throw new ArgumentNullException(nameof(ticker));
		}
	}

	public sealed class ImmediateMainThreadRunner : IMainThreadRunner
	{
		public void RunInMainThread(Action action)
		{
			action?.Invoke();
		}
	}

	public sealed class NullApplicationInfo : IApplicationInfo
	{
		public string ApplicationIdentifier { get; } = string.Empty;
		public string ApplicationVersion { get; } = string.Empty;
		public string ApplicationPlatform { get; } = string.Empty;
		public string DeviceIdentifier { get; } = string.Empty;
		public string PersistentDataPath { get; } = string.Empty;
		public string StreamingAssetsPath { get; } = string.Empty;
		public string DeviceManufacturer { get; } = string.Empty;
		public string OperatingSystemFamily { get; } = string.Empty;
		public string OperatingSystemVersion { get; } = string.Empty;
	}

	public sealed class NullStopwatchFactory : IStopwatchFactory
	{
		public IStopwatch Create() => new NullStopwatch();
	}

	public sealed class NullStopwatch : IStopwatch
	{
		public TimeSpan Elapsed => TimeSpan.Zero;
		public bool IsRunning => false;
		public void Reset() { }
		public void Restart() { }
		public void Start() { }
		public void Stop() { }
	}

	public sealed class NullInternetReachability : IInternetReachability
	{
		public bool IsInternetAvailable => false;
	}

	public sealed class NullTicker : ITicker
	{
		public event Action OnTick;
	}
}
