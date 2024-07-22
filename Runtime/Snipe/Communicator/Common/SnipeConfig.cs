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
		}
		public WebSocketImplementations WebSocketImplementation = WebSocketImplementations.WebSocketSharp;

		public string ContextId { get; }

		public SnipeProjectInfo Project => _project;
		public string ClientKey => _project.ClientKey;

		public string ProjectName { get; private set; }
		public string AppInfo { get; private set; }
		public string DebugId { get; private set; }
		
		public bool AutoJoinRoom { get; set; } = true;

		public List<string> ServerWebSocketUrls { get; } = new List<string>();
		public List<UdpAddress> ServerUdpUrls { get; } = new List<UdpAddress>();
		public string ServerHttpUrl { get; set; }

		/// <summary>
		/// Http transport heartbeat interval.
		/// If the value is less than 1 second then heartbeat is turned off.
		/// </summary>
		public TimeSpan HttpHeartbeatInterval { get; set; } = TimeSpan.Zero;
		
		public bool CompressionEnabled { get; set; } = true;
		public int MinMessageBytesToCompress { get; set; } = 13 * 1024;

		public IDictionary<string, object> LoginParameters { get; set; }

		public string LogReporterUrl { get; set; }

		private SnipeProjectInfo _project;

		private int _serverWebSocketUrlIndex = 0;
		private int _serverUdpUrlIndex = 0;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly IApplicationInfo _applicationInfo;

		public SnipeConfig(string contextId)
		{
			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_applicationInfo = SnipeServices.ApplicationInfo;

			ContextId = contextId ?? "";
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

			ServerHttpUrl = "https://dev.snipe.dev/";
			HttpHeartbeatInterval = TimeSpan.Zero;

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

			ServerHttpUrl = "https://live.snipe.dev/";
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
			if (SnipeObject.TryGetValue(data, "snipeUdpHost", out string udpHost) && !string.IsNullOrEmpty(udpHost) &&
				SnipeObject.TryGetValue(data, "snipeUdpPort", out string udpPort) && ushort.TryParse(udpPort, out ushort port))
			{
				ServerUdpUrls.Clear();
				ServerUdpUrls.Add(new UdpAddress() { Host = udpHost, Port = port });
			}

			if (SnipeObject.TryGetValue(data, "snipeHttpUrl", out string httpUrl) && !string.IsNullOrEmpty(httpUrl))
			{
				ServerHttpUrl = httpUrl;
			}

			if (SnipeObject.TryGetValue(data, "snipeWssUrl", out object wssUrl))
			{
				List<string> outputList = ServerWebSocketUrls;
				ParseWebSocketUrls(outputList, wssUrl);
			}

			if (SnipeObject.TryGetValue(data, "snipeDev", out bool dev))
			{
				_project.Mode = dev ? SnipeProjectMode.Dev : SnipeProjectMode.Live;
			}

			ParseLogReporterSection(data);
			ParseCompressionSection(data);
		}

		// [Testable]
		internal static void ParseWebSocketUrls(List<string> outputList, object wssUrl)
		{
			if (wssUrl is IList wssUrlList && wssUrlList.Count > 0)
			{
				SetWebSocketUrls(outputList, wssUrlList);
			}
			else if (wssUrl is string wssUrlString && !string.IsNullOrEmpty(wssUrlString))
			{
				string lowerUrl = wssUrlString.ToLower();

				if (lowerUrl.StartsWith('['))
				{
					IList list;
					try
					{
						list = (IList)JSON.Parse(wssUrlString);
					}
					catch (Exception)
					{
						list = null;
					}

					if (list != null && list.Count > 0)
					{
						SetWebSocketUrls(outputList, list);
					}
				}
				else if (lowerUrl.StartsWith("wss://"))
				{
					outputList.Clear();
					outputList.Add(wssUrlString);
				}
			}
		}

		private static void SetWebSocketUrls(List<string> outputList, IList wssUrlList)
		{
			outputList.Clear();

			foreach (var listItem in wssUrlList)
			{
				if (listItem is string url && !string.IsNullOrEmpty(url) && url.ToLower().StartsWith("wss://"))
				{
					outputList.Add(url);
				}
			}
		}


		/*
		private void ParseOld(IDictionary<string, object> data)
		{
			_project.ClientKey = SnipeObject.SafeGetString(data, "client_key").Trim();

			if (ServerWebSocketUrls == null)
				ServerWebSocketUrls = new List<string>();
			else
				ServerWebSocketUrls.Clear();

			if (data.TryGetValue("server_urls", out var server_urls_field) &&
				server_urls_field is IList server_ulrs_list)
			{
				foreach (string url in server_ulrs_list)
				{
					if (!string.IsNullOrWhiteSpace(url))
					{
						ServerWebSocketUrls.Add(url.Trim());
					}
				}
			}

			if (ServerWebSocketUrls.Count < 1)
			{
				// "service_websocket" field for backward compatibility
				var service_url = SnipeObject.SafeGetString(data, "service_websocket");
				if (!string.IsNullOrWhiteSpace(service_url))
				{
					ServerWebSocketUrls.Add(service_url.Trim());
				}
			}

			if (ServerUdpUrls == null)
				ServerUdpUrls = new List<UdpAddress>();
			else
				ServerUdpUrls.Clear();

			if (data.TryGetValue("server_udp_urls", out var server_udp_urls_field) &&
				server_udp_urls_field is IList server_udp_list)
			{
				foreach (string item in server_udp_list)
				{
					if (TryParseUdpUrl(item, out UdpAddress address))
					{
						ServerUdpUrls.Add(address);
					}
				}
			}

			if (ServerUdpUrls.Count < 1)
			{
				// backward compatibility

				var address = new UdpAddress()
				{
					Host = SnipeObject.SafeGetString(data, "server_udp_address").Trim(),
					Port = SnipeObject.SafeGetValue<ushort>(data, "server_udp_port"),
				};

				if (address.Port > 0 && !string.IsNullOrWhiteSpace(address.Host))
				{
					ServerUdpUrls.Add(address);
				}
			}

			if (SnipeObject.TryGetValue(data, "server_http_address", out string httpUrl))
			{
				if (!string.IsNullOrWhiteSpace(httpUrl) && httpUrl.StartsWith("http"))
				{
					ServerHttpUrl = httpUrl;
				}

				if (SnipeObject.TryGetValue(data, "server_http_heartbeat_seconds", out int heartbeatInterval))
				{
					HttpHeartbeatInterval = TimeSpan.FromSeconds(heartbeatInterval);
				}
			}

			ParseLogReporterSection(data);
			ParseCompressionSection(data);

			InitializeUrlIndices();

			TablesConfig.Init(data);

			InitAppInfo();
		}

		private bool TryParseUdpUrl(string url, out UdpAddress udpAddress)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				udpAddress = null;
				return false;
			}

			url = url.Trim();

			int index = url.IndexOf("://");
			if (index >= 0)
			{
				url = url.Substring(index + 3);
			}

			index = url.IndexOf("?");
			if (index >= 0)
			{
				url = url.Substring(0, index);
			}

			string[] address = url.Split(':');
			if (address.Length >= 2)
			{
				string port_string = address[address.Length - 1];

				if (ushort.TryParse(port_string, out ushort port) && port > 0)
				{
					string host = url.Substring(0, url.Length - port_string.Length - 1);
					udpAddress = new UdpAddress() { Host = host, Port = port };
					return true;
				}
			}

			udpAddress = null;
			return false;
		}
		*/

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
			if (data.TryGetValue("compression", out var compression_field) &&
				compression_field is IDictionary<string, object> compression)
			{
				CompressionEnabled = SnipeObject.SafeGetValue<bool>(compression, "enabled");

				if (CompressionEnabled)
				{
					if (SnipeObject.TryGetValue(compression, "min_size", out int minSize) ||
						SnipeObject.TryGetValue(compression, "minSize", out minSize))
					{
						MinMessageBytesToCompress = minSize;
					}
				}
			}
		}

		private void InitializeAppInfo()
		{
			AppInfo = new SnipeObject()
			{
				["identifier"] = _applicationInfo.ApplicationIdentifier,
				["version"] = _applicationInfo.ApplicationVersion,
				["platform"] = _applicationInfo.ApplicationPlatform,
				["packageVersion"] = PackageInfo.VERSION_CODE,
				["packageVersionName"] = PackageInfo.VERSION_NAME,
			}.ToJSONString();

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
			return ServerHttpUrl;
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
			return !string.IsNullOrEmpty(GetHttpAddress());
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

