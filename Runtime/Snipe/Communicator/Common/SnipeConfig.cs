using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MiniIT;

public static class SnipeConfig
{
	public class UdpAddress
	{
		public string Host;
		public ushort Port;
	}

	public static string ClientKey;
	public static string AppInfo;

	public static List<string> ServerWebSocketUrls = new List<string>();
	public static List<UdpAddress> ServerUdpUrls = new List<UdpAddress>();
	public static List<string> TablesUrls = new List<string>();
	
	public static bool CompressionEnabled = false;
	public static int MinMessageSizeToCompress = 10240; // bytes
	
	public static SnipeObject LoginParameters;
	public static bool TablesUpdateEnabled = true;

	public static string LogReporterKey;
	public static string LogReporterUrl;
	
	public static string PersistentDataPath { get; private set; }
	public static string StreamingAssetsPath { get; private set; }
	
	private static int _serverWebSocketUrlIndex = 0;
	private static int _serverUdpUrlIndex = 0;
	private static int _tablesUrlIndex = 0;

	/// <summary>
	/// Should be called from the main Unity thread
	/// </summary>
	public static void InitFromJSON(string json_string)
	{
		Init(SnipeObject.FromJSONString(json_string));
	}

	/// <summary>
	/// Should be called from the main Unity thread
	/// </summary>
	public static void Init(SnipeObject data)
	{
		ClientKey = data.SafeGetString("client_key");
		
		if (ServerWebSocketUrls == null)
			ServerWebSocketUrls = new List<string>();
		else
			ServerWebSocketUrls.Clear();
		
		if (data["server_urls"] is IList server_ulrs_list)
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
			var service_url = data.SafeGetString("service_websocket");
			if (!string.IsNullOrEmpty(service_url))
			{
				ServerWebSocketUrls.Add(service_url);
			}
		}

		if (ServerUdpUrls == null)
			ServerUdpUrls = new List<UdpAddress>();
		else
			ServerUdpUrls.Clear();

		if (data["server_udp_urls"] is IList server_udp_list)
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
				Host = data.SafeGetString("server_udp_address"),
				Port = data.SafeGetValue<ushort>("server_udp_port"),
			};

			if (address.Port > 0 && !string.IsNullOrEmpty(address.Host))
			{
				ServerUdpUrls.Add(address);
			}
		}

		if (TablesUrls == null)
			TablesUrls = new List<string>();
		else
			TablesUrls.Clear();
		
		if (data["tables_path"] is IList tables_ulrs_list)
		{
			foreach (string path in tables_ulrs_list)
			{
				var corrected_path = path.Trim();
				if (!corrected_path.EndsWith("/"))
					corrected_path += "/";
				
				TablesUrls.Add(corrected_path);
			}
		}

		if (data["log_reporter"] is SnipeObject log_reporter)
		{
			LogReporterKey = log_reporter.SafeGetString("key");
			LogReporterUrl = log_reporter.SafeGetString("url");
		}

		if (data["compression"] is SnipeObject compression)
		{
			CompressionEnabled = compression.SafeGetValue<bool>("enabled");
			MinMessageSizeToCompress = compression.SafeGetValue<int>("min_size");
		}
		
		_serverWebSocketUrlIndex = 0;
		_tablesUrlIndex = -1;

		PersistentDataPath = Application.persistentDataPath;
		StreamingAssetsPath = Application.streamingAssetsPath;

		InitAppInfo();
	}
	
	private static void InitAppInfo()
	{
		AppInfo = new SnipeObject()
		{
			["identifier"] = Application.identifier,
			["version"] = Application.version,
			["platform"] = Application.platform.ToString(),
		}.ToJSONString();
	}
	
	public static string GetWebSocketUrl()
	{
		_serverWebSocketUrlIndex = GetValidIndex(ServerWebSocketUrls, _serverWebSocketUrlIndex, false);
		if (_serverWebSocketUrlIndex >= 0)
		{
			return ServerWebSocketUrls[_serverWebSocketUrlIndex];
		}

		return null;
	}

	public static UdpAddress GetUdpAddress()
	{
		_serverUdpUrlIndex = GetValidIndex(ServerUdpUrls, _serverUdpUrlIndex, false);
		if (_serverUdpUrlIndex >= 0)
		{
			return ServerUdpUrls[_serverUdpUrlIndex];
		}

		return null;
	}

	public static void NextWebSocketUrl()
	{
		_serverWebSocketUrlIndex = GetValidIndex(ServerWebSocketUrls, _serverWebSocketUrlIndex, true);
	}

	public static bool NextUdpUrl()
	{
		int prev = _serverUdpUrlIndex;
		_serverUdpUrlIndex = GetValidIndex(ServerUdpUrls, _serverUdpUrlIndex, true);
		return _serverUdpUrlIndex > prev;
	}

	public static string GetTablesPath(bool next = false)
	{
		_tablesUrlIndex = GetValidIndex(TablesUrls, _tablesUrlIndex, next);
		if (_tablesUrlIndex >= 0)
		{
			return TablesUrls[_tablesUrlIndex];
		}

		return null;
	}
	
	public static bool CheckUdpAvailable()
	{
		if (ServerUdpUrls == null || ServerUdpUrls.Count < 1)
			return false;
		var address = ServerUdpUrls[0];
		return !string.IsNullOrEmpty(address?.Host) && address.Port > 0;
	}
	
	private static int GetValidIndex(IList list, int index, bool next = false)
	{
		if (list != null && list.Count > 0)
		{
			if (next)
			{
				if (index < list.Count - 1)
					index++;
				else
					index = 0;
			}
			
			if (index < 0)
			{
				index = 0;
			}
			
			return index;
		}

		return -1;
	}
}

