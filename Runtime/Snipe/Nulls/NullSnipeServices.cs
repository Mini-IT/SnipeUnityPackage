using System;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniIT.Http;
using MiniIT.Snipe.Logging;
using MiniIT.Storage;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public sealed class NullSnipeServices : ISnipeServices
	{
		public ISharedPrefs SharedPrefs { get; }
		public ILogService LogService { get; }
		public ISnipeAnalyticsService Analytics { get; }
		public IMainThreadRunner MainThreadRunner { get; }
		public IApplicationInfo ApplicationInfo { get; }
		public IStopwatchFactory FuzzyStopwatchFactory { get; }
		public IHttpClientFactory HttpClientFactory { get; }
		public IInternetReachabilityProvider InternetReachabilityProvider { get; }
		public ITicker Ticker { get; }

		public NullSnipeServices()
			: this(new NullSharedPrefs(),
				new NullLogService(),
				new NullSnipeAnalyticsService(),
				new ImmediateMainThreadRunner(),
				new NullApplicationInfo(),
				new NullStopwatchFactory(),
				new NullHttpClientFactory(),
				new NullInternetReachabilityProvider(),
				new NullTicker())
		{
		}

		public NullSnipeServices(
			ISharedPrefs sharedPrefs,
			ILogService logService,
			ISnipeAnalyticsService analytics,
			IMainThreadRunner mainThreadRunner,
			IApplicationInfo applicationInfo,
			IStopwatchFactory fuzzyStopwatchFactory,
			IHttpClientFactory httpClientFactory,
			IInternetReachabilityProvider internetReachabilityProvider,
			ITicker ticker)
		{
			SharedPrefs = sharedPrefs ?? throw new ArgumentNullException(nameof(sharedPrefs));
			LogService = logService ?? throw new ArgumentNullException(nameof(logService));
			Analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
			MainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
			ApplicationInfo = applicationInfo ?? throw new ArgumentNullException(nameof(applicationInfo));
			FuzzyStopwatchFactory = fuzzyStopwatchFactory ?? throw new ArgumentNullException(nameof(fuzzyStopwatchFactory));
			HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
			InternetReachabilityProvider = internetReachabilityProvider ?? throw new ArgumentNullException(nameof(internetReachabilityProvider));
			Ticker = ticker ?? throw new ArgumentNullException(nameof(ticker));
		}
	}

	public sealed class NullLogService : ILogService
	{
		public ILogger<T> GetLogger<T>() where T : class => NullLogger<T>.Instance;
		public ILogger GetLogger(string categoryName) => NullLogger.Instance;
	}

	public sealed class NullSnipeAnalyticsService : ISnipeAnalyticsService
	{
		private readonly SnipeAnalyticsService _inner = new SnipeAnalyticsService();

		public bool IsEnabled
		{
			get => false;
			set { }
		}

		public ISnipeAnalyticsTracker GetTracker(int contextId = 0)
		{
			_inner.IsEnabled = false;
			return _inner.GetTracker(contextId);
		}

		public void SetTracker(ISnipeCommunicatorAnalyticsTracker externalTracker)
		{
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

	public sealed class NullHttpClientFactory : IHttpClientFactory
	{
		public IHttpClient CreateHttpClient() => new NullHttpClient();
	}

	public sealed class NullHttpClient : IHttpClient
	{
		public void Reset() { }
		public void SetAuthToken(string token) { }
		public void SetPersistentClientId(string token) { }
		public UniTask<IHttpClientResponse> Get(Uri uri) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
		public UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
		public UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
		public UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
	}

	public sealed class NullHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode => 0;
		public bool IsSuccess => false;
		public string Error => "NullHttpClient";

		public UniTask<string> GetStringContentAsync() => UniTask.FromResult(string.Empty);
		public UniTask<byte[]> GetBinaryContentAsync() => UniTask.FromResult(Array.Empty<byte>());
		public void Dispose() { }
	}

	public sealed class NullInternetReachabilityProvider : IInternetReachabilityProvider
	{
		public bool IsInternetAvailable => false;
	}

	public sealed class NullTicker : ITicker
	{
		public event Action OnTick;
	}

	public sealed class NullSharedPrefs : ISharedPrefs
	{
		public bool HasKey(string key) => false;
		public void DeleteKey(string key) { }
		public void DeleteAll() { }
		public void Save() { }
		public bool GetBool(string key, bool defaultValue = false) => defaultValue;
		public float GetFloat(string key, float defaultValue = 0) => defaultValue;
		public int GetInt(string key, int defaultValue = 0) => defaultValue;
		public string GetString(string key, string defaultValue = null) => defaultValue;
		public void SetBool(string key, bool value) { }
		public void SetFloat(string key, float value) { }
		public void SetInt(string key, int value) { }
		public void SetString(string key, string value) { }
	}
}
