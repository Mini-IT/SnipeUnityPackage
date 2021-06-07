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

	public string ServiceWebsocketURL;
	
	public List<string> TablesURLs = new List<string>();
	public string PersistentDataPath { get; private set; }
	public string StreamingAssetsPath { get; private set; }
	
	private int mTablesURLIndex = 0;

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
		Instance.ClientKey = data.SafeGetString("client_key");
		Instance.ServiceWebsocketURL = data.SafeGetString("service_websocket");
		
 		if (Instance.TablesURLs == null)
			Instance.TablesURLs = new List<string>();
		else
			Instance.TablesURLs.Clear();
		
		Instance.mTablesURLIndex = -1;
		
		if (data["tables_path"] is IList list)
		{
			foreach(string path in list)
			{
				var corrected_path = path.Trim();
				if (!corrected_path.EndsWith("/"))
					corrected_path += "/";
				
				Instance.TablesURLs.Add(corrected_path);
			}
		}

		Instance.PersistentDataPath = Application.persistentDataPath;
		Instance.StreamingAssetsPath = Application.streamingAssetsPath;

		Instance.InitAppInfo();
	}
	
	private void InitAppInfo()
	{
		this.AppInfo = new SnipeObject()
		{
			["identifier"] = Application.identifier,
			["version"] = Application.version,
			["platform"] = Application.platform.ToString(),
		}.ToJSONString();
	}

	public string GetTablesPath(bool next = false)
	{
		if (TablesURLs != null && TablesURLs.Count > 0)
		{
			if (mTablesURLIndex < 0)
			{
				mTablesURLIndex = 0;
			}
			else if (next)
			{
				mTablesURLIndex++;
				if (mTablesURLIndex >= TablesURLs.Count)
					mTablesURLIndex = 0;
			}
			
			return TablesURLs[mTablesURLIndex];
		}

		return null;
	}
}

