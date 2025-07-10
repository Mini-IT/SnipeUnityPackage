using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using fastJSON;

namespace MiniIT.Snipe
{
	public enum SnipeProjectMode
	{
		Live = 0,
		Dev = 1,
	}

	public struct SnipeProjectInfo
	{
		public string ProjectID;
		public string ClientKey;
		public SnipeProjectMode Mode;
	}

	public class SnipeConfig
	{
		public class UdpAddress
		{
			public string Host;
			public ushort Port;
		}

		public enum WebSocketImplementations
		{
			WebSocketSharp,
			ClientWebSocket,
			BestWebSocket,
		}

		public WebSocketImplementations WebSocketImplementation { get; set; } = WebSocketImplementations.WebSocketSharp;

		public int ContextId { get; }

		public SnipeProjectInfo Project => _project;
		public string ClientKey => _project.ClientKey;

		public string ProjectName { get; private set; }
		public string AppInfo { get; private set; }
		public string DebugId { get; private set; }

		public bool AutoJoinRoom { get; set; } = true;

		public List<string> ServerWebSocketUrls { get; } = new List<string>();
		public List<UdpAddress> ServerUdpUrls { get; } = new List<UdpAddress>();
		public List<string> ServerHttpUrls { get; } = new List<string>();

		/// <summary>
		/// Http transport heartbeat interval.
		/// If the value is less than 1 second then heartbeat is turned off.
		/// </summary>
		public TimeSpan HttpHeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

		public bool CompressionEnabled { get; set; } = true;
		public int MinMessageBytesToCompress { get; set; } = 13 * 1024;

		public IDictionary<string, object> LoginParameters { get; set; }

		public string LogReporterUrl { get; set; }

		private SnipeProjectInfo _project;

		private int _serverWebSocketUrlIndex = 0;
		private int _serverUdpUrlIndex = 0;
		private int _serverHttpUrlIndex = 0;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly IApplicationInfo _applicationInfo;

		public SnipeConfig(int contextId)
		{
			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_applicationInfo = SnipeServices.ApplicationInfo;

			ContextId = contextId;
		}

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		//public void InitializeFromJSON(string json_string)
		//{
		//	Initialize(SnipeObject.FromJSONString(json_string));
		//}

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		//public void Initialize(IDictionary<string, object> data)
		//{
		//	ParseOld(data);
		//	ParseNew(data);
		//}

		public void Initialize(SnipeProjectInfo project, IDictionary<string, object> data)
		{
			if (data == null || data.Count == 0)
			{
				InitializeDefault(project);
				return;
			}

			SetProject(project);

			ParseNew(data);
			ParseLogReporterSection(data);
			ParseCompressionSection(data);

			if (!CheckConnectionParametersValid())
			{
				InitializeDefault(project);
				return;
			}

			if (project.Mode == SnipeProjectMode.Dev)
			{
				InitializeDefaultTablesConfigDev();
			}
			else
			{
				InitializeDefaultTablesConfigLive();
			}

			InitializeUrlIndices();

			InitializeAppInfo();
		}

		/// <summary>
		/// Initialize with default values
		/// </summary>
		public void InitializeDefault(SnipeProjectInfo project)
		{
			SetProject(project);

			if (project.Mode == SnipeProjectMode.Dev)
			{
				InitializeDefaultConnectionDev();
				InitializeDefaultTablesConfigDev();
			}
			else
			{
				InitializeDefaultConnectionLive();
				InitializeDefaultTablesConfigLive();
			}

			InitializeUrlIndices();

			InitializeAppInfo();
		}

		private void SetProject(SnipeProjectInfo project)
		{
			_project = project;

			ProjectName = (project.Mode == SnipeProjectMode.Dev) ?
				$"{project.ProjectID}_dev" :
				$"{project.ProjectID}_live";
		}

		#region Default config

		private void InitializeDefaultConnectionDev()
		{
			ServerUdpUrls.Clear();
			ServerUdpUrls.Add(new UdpAddress() { Host = "dev.snipe.dev", Port = 10666 });

			ServerWebSocketUrls.Clear();
			ServerWebSocketUrls.Add("wss://dev.snipe.dev/wss_11000/");
			ServerWebSocketUrls.Add("wss://dev-proxy.snipe.dev/wss_11000/");
			ServerWebSocketUrls.Add("wss://dev2.snipe.dev/wss_11000/");
			ServerWebSocketUrls.Add("wss://dev-proxy2.snipe.dev/wss_11000/");

			ServerHttpUrls.Clear();
			ServerHttpUrls.Add("https://dev.snipe.dev/");
			HttpHeartbeatInterval = TimeSpan.FromMinutes(1);

			LogReporterUrl = "https://logs-dev.snipe.dev/api/v1/log/batch";
		}

		private void InitializeDefaultConnectionLive()
		{
			ServerUdpUrls.Clear();
			ServerUdpUrls.Add(new UdpAddress() { Host = "live.snipe.dev", Port = 16666 });

			ServerWebSocketUrls.Clear();
			ServerWebSocketUrls.Add("wss://live.snipe.dev/wss_16000/");
			ServerWebSocketUrls.Add("wss://live-proxy.snipe.dev/wss_16000/");
			ServerWebSocketUrls.Add("wss://live2.snipe.dev/wss_16000/");
			ServerWebSocketUrls.Add("wss://live-proxy2.snipe.dev/wss_16000/");

			ServerHttpUrls.Clear();
			ServerHttpUrls.Add("https://live.snipe.dev/");
			HttpHeartbeatInterval = TimeSpan.Zero;

			LogReporterUrl = "https://logs.snipe.dev/api/v1/log/batch";
		}

		private void InitializeUrlIndices()
		{
			_serverWebSocketUrlIndex = SnipeServices.SharedPrefs.GetInt(SnipePrefs.GetWebSocketUrlIndex(ContextId), 0);
			_serverUdpUrlIndex = SnipeServices.SharedPrefs.GetInt(SnipePrefs.GetUdpUrlIndex(ContextId), 0);
		}

		private void InitializeDefaultTablesConfigDev()
		{
			TablesConfig.ResetTablesUrls();
			TablesConfig.AddTableUrl($"https://static-dev.snipe.dev/{ProjectName}/");
			TablesConfig.AddTableUrl($"https://static-dev-noproxy.snipe.dev/{ProjectName}/");
		}

		private void InitializeDefaultTablesConfigLive()
		{
			TablesConfig.ResetTablesUrls();
			TablesConfig.AddTableUrl($"https://static.snipe.dev/{ProjectName}/");
			TablesConfig.AddTableUrl($"https://static-noproxy.snipe.dev/{ProjectName}/");
			TablesConfig.AddTableUrl($"https://snipe.tuner-life.com/{ProjectName}/");
		}

		#endregion Default config

		private void ParseNew(IDictionary<string, object> data)
		{
			if (SnipeObject.TryGetValue(data, "snipeUdpHost", out string udpHost) && !string.IsNullOrWhiteSpace(udpHost) &&
				SnipeObject.TryGetValue(data, "snipeUdpPort", out string udpPort) && ushort.TryParse(udpPort, out ushort port))
			{
				ServerUdpUrls.Clear();
				ServerUdpUrls.Add(new UdpAddress() { Host = udpHost.Trim(), Port = port });
			}

			if (data.TryGetValue("snipeHttpUrls", out object httpUrls))
			{
				ParseHttpUrls(ServerHttpUrls, httpUrls);
			}
			else if (SnipeObject.TryGetValue(data, "snipeHttpUrl", out string httpUrl) && !string.IsNullOrWhiteSpace(httpUrl))
			{
				ServerHttpUrls.Clear();
				ServerHttpUrls.Add(httpUrl.Trim());
			}

			if (SnipeObject.TryGetValue(data, "snipeWssUrl", out object wssUrl))
			{
				ParseWebSocketUrls(ServerWebSocketUrls, wssUrl);
			}

			if (SnipeObject.TryGetValue(data, "snipeDev", out bool dev))
			{
				_project.Mode = dev ? SnipeProjectMode.Dev : SnipeProjectMode.Live;
			}

			ParseLogReporterSection(data);
			ParseCompressionSection(data);
		}

		// [Testable]
		internal static void ParseWebSocketUrls(List<string> outputList, object input)
		{
			ParseUrls(outputList, input, (url) => url.ToLower().StartsWith("wss://"));
		}

		// [Testable]
		internal static void ParseHttpUrls(List<string> outputList, object input)
		{
			ParseUrls(outputList, input, (url) => url.ToLower().StartsWith("https://"));
		}

		private static void ParseUrls(List<string> outputList, object input, Func<string, bool> urlChecker)
		{
			if (input is IList urlList && urlList.Count > 0)
			{
				SetUrls(outputList, urlList, urlChecker);
			}
			else if (input is string urlString && !string.IsNullOrWhiteSpace(urlString))
			{
				urlString = urlString.Trim();
				string lowerUrl = urlString.ToLower();

				if (lowerUrl.StartsWith('['))
				{
					IList list;
					try
					{
						list = (IList)JSON.Parse(urlString);
					}
					catch (Exception)
					{
						list = null;
					}

					if (list != null && list.Count > 0)
					{
						SetUrls(outputList, list, urlChecker);
					}
				}
				else if (urlChecker.Invoke(lowerUrl))
				{
					outputList.Clear();
					outputList.Add(urlString);
				}
			}
		}

		private static void SetUrls(List<string> outputList, IList wssUrlList, Func<string, bool> urlChecker)
		{
			outputList.Clear();

			foreach (var listItem in wssUrlList)
			{
				if (listItem is string url && !string.IsNullOrWhiteSpace(url))
				{
					url = url.Trim();
					if (urlChecker.Invoke(url))
					{
						outputList.Add(url);
					}
				}
			}
		}

		private void ParseLogReporterSection(IDictionary<string, object> data)
		{
			if (data.TryGetValue("log_reporter", out var logReporterField))
			{
				if (logReporterField is IDictionary<string, object> logReporterSection)
				{
					LogReporterUrl = SnipeObject.SafeGetString(logReporterSection, "url").Trim();
				}
				else if (logReporterField is string logReporterString)
				{
					var regex = new Regex(@"^http.?://.*", RegexOptions.IgnoreCase); // == StartsWith("http(s)://")

					if (regex.IsMatch(logReporterString))
					{
						LogReporterUrl = logReporterString;
					}
					else
					{
						var dict = (Dictionary<string, object>)JSON.Parse(logReporterString);
						if (dict != null)
						{
							LogReporterUrl = SnipeObject.SafeGetString(dict, "url").Trim();
						}
					}
				}
			}
		}

		private void ParseCompressionSection(IDictionary<string, object> data)
		{
			if (!data.TryGetValue("compression", out var compressionField) ||
			    compressionField is not IDictionary<string, object> compression)
			{
				return;
			}

			CompressionEnabled = SnipeObject.SafeGetValue<bool>(compression, "enabled");

			if (!CompressionEnabled)
			{
				return;
			}

			if (SnipeObject.TryGetValue(compression, "min_size", out int minSize) ||
			    SnipeObject.TryGetValue(compression, "minSize", out minSize))
			{
				MinMessageBytesToCompress = minSize;
			}
		}

		private void InitializeAppInfo()
		{
			var appInfo = new SnipeObject()
			{
				["identifier"] = _applicationInfo.ApplicationIdentifier,
				["version"] = _applicationInfo.ApplicationVersion,
				["platform"] = _applicationInfo.ApplicationPlatform,
				["packageVersion"] = PackageInfo.VERSION_CODE,
				["packageVersionName"] = PackageInfo.VERSION_NAME,
			};

			// ReSharper disable once SuspiciousTypeConversion.Global
			if (_applicationInfo is ISystemInfo systemInfo)
			{
				appInfo["deviceName"] = systemInfo.DeviceManufacturer;
				appInfo["osName"] = systemInfo.OperatingSystemFamily;
				appInfo["osVersion"] = $"{systemInfo.OperatingSystemVersion.Major}.{systemInfo.OperatingSystemVersion.Minor}";
			}

			AppInfo = appInfo.ToJSONString();

			DebugId = GenerateDebugId();
			SnipeServices.Analytics.GetTracker(ContextId).SetDebugId(DebugId);

		}

		private string GenerateDebugId()
		{
			using (var md5 = System.Security.Cryptography.MD5.Create())
			{
				string id = _applicationInfo.DeviceIdentifier + _applicationInfo.ApplicationIdentifier;
				byte[] hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id));
				string hash = Convert.ToBase64String(hashBytes);
				hash = new Regex(@"\W").Replace(hash, "0");
				return hash.Substring(0, Math.Min(16, hash.Length));
			}
		}

		public string GetWebSocketUrl()
		{
			_serverWebSocketUrlIndex = GetValidIndex(ServerWebSocketUrls, _serverWebSocketUrlIndex, false);
			if (_serverWebSocketUrlIndex >= 0)
			{
				return ServerWebSocketUrls[_serverWebSocketUrlIndex];
			}

			return null;
		}

		public UdpAddress GetUdpAddress()
		{
			_serverUdpUrlIndex = GetValidIndex(ServerUdpUrls, _serverUdpUrlIndex, false);
			if (_serverUdpUrlIndex >= 0)
			{
				return ServerUdpUrls[_serverUdpUrlIndex];
			}

			return null;
		}

		public string GetHttpAddress()
		{
			_serverHttpUrlIndex = GetValidIndex(ServerHttpUrls, _serverHttpUrlIndex, false);
			if (_serverHttpUrlIndex >= 0)
			{
				return ServerHttpUrls[_serverHttpUrlIndex];
			}

			return null;
		}

		public void NextWebSocketUrl()
		{
			_serverWebSocketUrlIndex = GetValidIndex(ServerWebSocketUrls, _serverWebSocketUrlIndex, true);

			_mainThreadRunner.RunInMainThread(() =>
			{
				string key = SnipePrefs.GetWebSocketUrlIndex(ContextId);
				SnipeServices.SharedPrefs.SetInt(key, _serverWebSocketUrlIndex);
			});
		}

		public bool NextUdpUrl()
		{
			int prev = _serverUdpUrlIndex;
			_serverUdpUrlIndex = GetValidIndex(ServerUdpUrls, _serverUdpUrlIndex, true);

			_mainThreadRunner.RunInMainThread(() =>
			{
				string key = SnipePrefs.GetUdpUrlIndex(ContextId);
				SnipeServices.SharedPrefs.SetInt(key, _serverUdpUrlIndex);
			});

			return _serverUdpUrlIndex > prev;
		}

		public bool NextHttpUrl()
		{
			int prev = _serverHttpUrlIndex;
			_serverHttpUrlIndex = GetValidIndex(ServerHttpUrls, _serverHttpUrlIndex, true);

			_mainThreadRunner.RunInMainThread(() =>
			{
				string key = SnipePrefs.GetHttpUrlIndex(ContextId);
				SnipeServices.SharedPrefs.SetInt(key, _serverHttpUrlIndex);
			});

			return _serverHttpUrlIndex > prev;
		}

		public bool CheckUdpAvailable()
		{
			if (ServerUdpUrls == null || ServerUdpUrls.Count < 1)
			{
				return false;
			}

			UdpAddress address = ServerUdpUrls[0];
			return !string.IsNullOrEmpty(address?.Host) && address.Port > 0;
		}

		public bool CheckWebSocketAvailable()
		{
			return ServerWebSocketUrls != null && ServerWebSocketUrls.Count > 0;
		}

		public bool CheckHttpAvailable()
		{
			string url = GetHttpAddress();

			if (string.IsNullOrEmpty(url))
			{
				return false;
			}

			return new Regex("^https?://..*\\..*").IsMatch(url);
		}

		private bool CheckConnectionParametersValid() =>
			CheckUdpAvailable() ||
			CheckWebSocketAvailable() ||
			CheckHttpAvailable();

		// [Testable]
		internal static int GetValidIndex(IList list, int index, bool next = false)
		{
			if (list == null || list.Count < 1)
			{
				return -1;
			}

			if (next)
			{
				if (index < list.Count - 1)
					index++;
				else
					index = 0;
			}
			else if (index < 0 || index >= list.Count)
			{
				index = 0;
			}

			return index;
		}
	}
}

