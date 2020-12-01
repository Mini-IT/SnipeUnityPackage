using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MiniIT;

public class SnipeConfig
{
	private static readonly SnipeConfig mInstance = new SnipeConfig();
	public static SnipeConfig Instance
	{
		get
		{
			return mInstance;
		}
	}

	public string ClientKey;
	public string AppInfo;

	public string AuthWebsocketURL;
	public string ServerWebsocketURL;
	public string ServiceWebsocketURL;
	
	public List<string> TablesURLs = new List<string>();
	public string PersistentDataPath { get; private set; }

	/// <summary>
	/// Should be called from the main Unity thread
	/// </summary>
	public static void InitFromJSON(string json_string)
	{
		Init(ExpandoObject.FromJSONString(json_string));
	}

	/// <summary>
	/// Should be called from the main Unity thread
	/// </summary>
	public static void Init(ExpandoObject data)
	{
		Instance.ClientKey = data.SafeGetString("client_key");
		Instance.AuthWebsocketURL = data.SafeGetString("auth_websocket");
		Instance.ServerWebsocketURL = data.SafeGetString("server_websocket");
		Instance.ServiceWebsocketURL = data.SafeGetString("service_websocket");
		
 		if (Instance.TablesURLs == null)
			Instance.TablesURLs = new List<string>();
		else
			Instance.TablesURLs.Clear();
		
		if (data["tables_path"] is IList list)
		{
			foreach(string path in list)
			{
				Instance.TablesURLs.Add(path);
			}
		}

		Instance.PersistentDataPath = Application.persistentDataPath;

		Instance.InitAppInfo();
	}
	
	private void InitAppInfo()
	{
		this.AppInfo = new ExpandoObject()
		{
			["identifier"] = Application.identifier,
			["version"] = Application.version,
			["platform"] = Application.platform.ToString(),
		}.ToJSONString();
	}

	public string GetTablesPath()
	{
		if (TablesURLs != null && TablesURLs.Count > 0)
			return TablesURLs[0];

		return null;
	}
}

