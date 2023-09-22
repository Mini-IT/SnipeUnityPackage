using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniIT.Snipe
{
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

		public string ClientKey;
		public string AppInfo;
		public string DebugId { get; private set; }

		public List<string> ServerWebSocketUrls = new List<string>();
		public List<UdpAddress> ServerUdpUrls = new List<UdpAddress>();
		
		public bool CompressionEnabled = true;
		public int MinMessageBytesToCompress = 13 * 1024;

		public SnipeObject LoginParameters;

		public string LogReporterUrl;

		public string PersistentDataPath { get; private set; }
		public string StreamingAssetsPath { get; private set; }

		private int _serverWebSocketUrlIndex = 0;
		private int _serverUdpUrlIndex = 0;

		private readonly TaskScheduler _mainThreadScheduler;

		public SnipeConfig(string contextId)
		{
			_mainThreadScheduler = SynchronizationContext.Current != null ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;

			ContextId = contextId ?? "";
		}

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void InitializeFromJSON(string json_string)
		{
			Initialize(SnipeObject.FromJSONString(json_string));
		}

		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void Initialize(IDictionary<string, object> data)
		{
			ClientKey = SnipeObject.SafeGetString(data, "client_key");

			if (ServerWebSocketUrls == null)
				ServerWebSocketUrls = new List<string>();
			else
				ServerWebSocketUrls.Clear();

			if (data.TryGetValue("server_urls", out var server_urls_field) &&
				server_urls_field is IList server_ulrs_list)
			{
				foreach (string url in server_ulrs_list)
				{
					if (!string.IsNullOrEmpty(url))
					{
						ServerWebSocketUrls.Add(url);
					}
				}
			}

			if (ServerWebSocketUrls.Count < 1)
			{
				// "service_websocket" field for backward compatibility
				var service_url = SnipeObject.SafeGetString(data, "service_websocket");
				if (!string.IsNullOrEmpty(service_url))
				{
					ServerWebSocketUrls.Add(service_url);
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
					string url = item;

					if (!string.IsNullOrEmpty(url))
					{
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
								ServerUdpUrls.Add(new UdpAddress() { Host = host, Port = port });
							}
						}
					}
				}
			}

			if (ServerUdpUrls.Count < 1)
			{
				// backward compatibility

				var address = new UdpAddress()
				{
					Host = SnipeObject.SafeGetString(data, "server_udp_address"),
					Port = SnipeObject.SafeGetValue<ushort>(data, "server_udp_port"),
				};

				if (address.Port > 0 && !string.IsNullOrEmpty(address.Host))
				{
					ServerUdpUrls.Add(address);
				}
			}

			if (data.TryGetValue("log_reporter", out var log_reporter_field) &&
				log_reporter_field is IDictionary<string, object> log_reporter)
			{
				LogReporterUrl = SnipeObject.SafeGetString(log_reporter, "url");
			}

			if (data.TryGetValue("compression", out var compression_field) &&
				compression_field is IDictionary<string, object> compression)
			{
				CompressionEnabled = SnipeObject.SafeGetValue<bool>(compression, "enabled");
				MinMessageBytesToCompress = SnipeObject.SafeGetValue<int>(compression, "min_size");
			}

			_serverWebSocketUrlIndex = SnipeServices.Instance.SharedPrefs.GetInt(SnipePrefs.GetWebSocketUrlIndex(ContextId), 0);
			_serverUdpUrlIndex = SnipeServices.Instance.SharedPrefs.GetInt(SnipePrefs.GetUdpUrlIndex(ContextId), 0);

			TablesConfig.Init(data);
			TablesConfig.PersistentDataPath = Application.persistentDataPath;

			InitAppInfo();
		}

		private void InitAppInfo()
		{
			AppInfo = new SnipeObject()
			{
				["identifier"] = Application.identifier,
				["version"] = Application.version,
				["platform"] = Application.platform.ToString(),
				["packageVersion"] = PackageInfo.VERSION,
			}.ToJSONString();

			using (var md5 = System.Security.Cryptography.MD5.Create())
			{
				string id = SystemInfo.deviceUniqueIdentifier + Application.identifier;
				byte[] hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id));
				DebugId = System.Convert.ToBase64String(hashBytes).Substring(0, 16);
				Analytics.GetInstance(ContextId).SetDebugId(DebugId);
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
			return "https://dev.snipe.dev/";
		}

		public void NextWebSocketUrl()
		{
			_serverWebSocketUrlIndex = GetValidIndex(ServerWebSocketUrls, _serverWebSocketUrlIndex, true);

			RunInMainThread(() => SnipeServices.Instance.SharedPrefs.SetInt(SnipePrefs.GetWebSocketUrlIndex(ContextId), _serverWebSocketUrlIndex));
		}

		public bool NextUdpUrl()
		{
			int prev = _serverUdpUrlIndex;
			_serverUdpUrlIndex = GetValidIndex(ServerUdpUrls, _serverUdpUrlIndex, true);

			RunInMainThread(() => SnipeServices.Instance.SharedPrefs.SetInt(SnipePrefs.GetUdpUrlIndex(ContextId), _serverUdpUrlIndex));

			return _serverUdpUrlIndex > prev;
		}

		public bool CheckUdpAvailable()
		{
			if (ServerUdpUrls == null || ServerUdpUrls.Count < 1)
				return false;
			var address = ServerUdpUrls[0];
			return !string.IsNullOrEmpty(address?.Host) && address.Port > 0;
		}

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

		private void RunInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}
	}
}

