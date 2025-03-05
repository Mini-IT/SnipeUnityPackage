using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using fastJSON;
using MiniIT.Snipe.Configuration;

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
		public string ServerHttpUrl => _data.ServerHttpUrl;

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
		private readonly IMainThreadRunner _mainThreadRunner;
		private readonly IApplicationInfo _applicationInfo;

		private readonly SnipeConfigData _data;

		public SnipeConfig(int contextId, SnipeConfigData data)
		{
			_mainThreadRunner = SnipeServices.MainThreadRunner;
			_applicationInfo = SnipeServices.ApplicationInfo;

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

			if (Project.Mode == SnipeProjectMode.Dev)
			{
				InitializeDefaultTablesConfigDev();
			}
			else
			{
				InitializeDefaultTablesConfigLive();
			}

			ProjectName = (_data.ProjectInfo.Mode == SnipeProjectMode.Dev) ?
				$"{_data.ProjectInfo.ProjectID}_dev" :
				$"{_data.ProjectInfo.ProjectID}_live";

			InitializeUrlIndices();
			InitializeAppInfo();
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

		private void InitializeAppInfo()
		{
			var appInfo = new Dictionary<string, object>()
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

			AppInfo = JsonUtility.ToJson(appInfo);

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

