using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using fastJSON;
using MiniIT.Utils;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeConfigFile
	{
		private const string CACHE_FILENAME = "snipe_config.json";
		private const string SA_FILENAME = "snipe_config.json";

		private readonly string _savedFilePath;
		private readonly string _builtinFilePath;
		private readonly TextFileLoader _textLoader;

		public SnipeConfigFile()
		{
			_savedFilePath = Path.Combine(Application.persistentDataPath, CACHE_FILENAME);
			_builtinFilePath = Path.Combine(Application.streamingAssetsPath, SA_FILENAME);
			_textLoader = new TextFileLoader();
		}

		public async UniTask LoadAndMerge(IDictionary<string, object> config)
		{
			string json = await _textLoader.ReadTextFromFile(_savedFilePath);
			if (TryParse(json, out var parsedConfig))
			{
				DictionaryUtility.Merge(config, parsedConfig);
			}

			if (config.Count == 0)
			{
				json = await _textLoader.ReadTextFromStreamingAssets(_builtinFilePath);
				if (TryParse(json, out parsedConfig))
				{
					DictionaryUtility.Merge(config, parsedConfig);
				}
				else
				{
					Debug.Log($"[{nameof(SnipeConfigFile)}] Failed to load config from StreamingAssets");
				}
			}
		}

		public bool TryParse(string json, out IDictionary<string, object> config)
		{
			if (string.IsNullOrEmpty(json))
			{
				config = null;
				return false;
			}

			try
			{
				var loadedConfig = (Dictionary<string, object>)JSON.Parse(json);
				if (loadedConfig != null)
				{
					config = loadedConfig;
					return true;
				}
			}
			catch (Exception e)
			{
				Debug.Log($"[{nameof(SnipeConfigFile)}] Failed to parse: {e}");
			}

			config = null;
			return false;
		}

		public void SaveConfig(IDictionary<string, object> config)
		{
			UniTask.RunOnThreadPool(() => SaveConfig(config, _savedFilePath), false);
		}

		private void SaveConfig(IDictionary<string, object> config, string path)
		{
			Debug.Log($"[{nameof(SnipeConfigFile)}] SaveConfig");

			try
			{
				string json = JSON.ToJSON(config);
				File.WriteAllText(path, json);
			}
			catch (Exception e)
			{
				Debug.Log($"[{nameof(SnipeConfigFile)}] Failed to save: {e}");
			}
		}
	}
}
