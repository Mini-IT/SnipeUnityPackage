using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MiniIT.Snipe.Configuration;
using MiniIT.Utils;

namespace MiniIT.Snipe
{
	public class SnipeConfig
	{
		public int ContextId { get; }

		public SnipeProjectInfo Project => _data.ProjectInfo;
		public string ClientKey => _data.ProjectInfo.ClientKey;

		public string ProjectName { get; private set; }
		public string AppInfo { get; private set; }
		public string DebugId { get; private set; }

		public bool AutoJoinRoom => _data.AutoJoinRoom;

		public List<string> ServerWebSocketUrls => _data.ServerWebSocketUrls;
		public List<UdpAddress> ServerUdpUrls => _data.ServerUdpUrls;
		public List<string> ServerHttpUrls => _data.ServerHttpUrls;

		public WebSocketImplementations WebSocketImplementation => _data.WebSocketImplementation;

		/// <summary>
		/// Http transport heartbeat interval.
		/// If the value is less than 1 second then heartbeat is turned off.
		/// </summary>
		public TimeSpan HttpHeartbeatInterval => _data.HttpHeartbeatInterval;

		public bool CompressionEnabled => _data.CompressionEnabled;
		public int MinMessageBytesToCompress => _data.MinMessageBytesToCompress;

		public IDictionary<string, object> LoginParameters => _data.LoginParameters;

		public string LogReporterUrl => _data.LogReporterUrl;

		private int _serverWebSocketUrlIndex = 0;
		private int _serverUdpUrlIndex = 0;
		private int _serverHttpUrlIndex = 0;
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly IApplicationInfo _applicationInfo;

		private readonly SnipeConfigData _data;

		public SnipeConfig(int contextId, SnipeConfigData data)
		{
			_mainThreadRunner = SnipeServices.Instance.MainThreadRunner;
			_applicationInfo = SnipeServices.Instance.ApplicationInfo;

			ContextId = contextId;
			_data = data;

			Initialize();
		}

		private void Initialize()
		{
			// if (!CheckConnectionParametersValid())
			// {
			// 	InitializeDefault(project);
			// 	return;
			// }

			ProjectName = (_data.ProjectInfo.Mode == SnipeProjectMode.Dev) ?
				$"{_data.ProjectInfo.ProjectID}_dev" :
				$"{_data.ProjectInfo.ProjectID}_live";

			if (Project.Mode == SnipeProjectMode.Dev)
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

		private void InitializeUrlIndices()
		{
			_serverWebSocketUrlIndex = SnipeServices.Instance.SharedPrefs.GetInt(SnipePrefs.GetWebSocketUrlIndex(ContextId), 0);
			_serverUdpUrlIndex = SnipeServices.Instance.SharedPrefs.GetInt(SnipePrefs.GetUdpUrlIndex(ContextId), 0);
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

		private void InitializeAppInfo()
		{
			var appInfo = new Dictionary<string, object>()
			{
				["identifier"] = _applicationInfo.ApplicationIdentifier,
				["version"] = _applicationInfo.ApplicationVersion,
				["platform"] = _applicationInfo.ApplicationPlatform,
				["packageVersion"] = PackageInfo.VERSION_CODE,
				["packageVersionName"] = PackageInfo.VERSION_NAME,
				["deviceName"] = _applicationInfo.DeviceManufacturer,
				["osName"] = _applicationInfo.OperatingSystemFamily,
				["osVersion"] = _applicationInfo.OperatingSystemVersion,
			};

			AppInfo = JsonUtility.ToJson(appInfo);

			DebugId = GenerateDebugId();
			SnipeServices.Instance.Analytics.GetTracker(ContextId).SetDebugId(DebugId);

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

		public bool NextWebSocketUrl()
		{
			int prev = _serverWebSocketUrlIndex;
			_serverWebSocketUrlIndex = GetValidIndex(ServerWebSocketUrls, _serverWebSocketUrlIndex, true);

			_mainThreadRunner.RunInMainThread(() =>
			{
				string key = SnipePrefs.GetWebSocketUrlIndex(ContextId);
				SnipeServices.Instance.SharedPrefs.SetInt(key, _serverWebSocketUrlIndex);
			});

			return _serverWebSocketUrlIndex > prev;
		}

		public bool NextUdpUrl()
		{
			int prev = _serverUdpUrlIndex;
			_serverUdpUrlIndex = GetValidIndex(ServerUdpUrls, _serverUdpUrlIndex, true);

			_mainThreadRunner.RunInMainThread(() =>
			{
				string key = SnipePrefs.GetUdpUrlIndex(ContextId);
				SnipeServices.Instance.SharedPrefs.SetInt(key, _serverUdpUrlIndex);
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
				SnipeServices.Instance.SharedPrefs.SetInt(key, _serverHttpUrlIndex);
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

