using System.Collections.Generic;
using MiniIT.Storage;

namespace MiniIT.Snipe.Tests.Editor
{
	internal class MockSharedPrefs : ISharedPrefs
	{
		private readonly Dictionary<string, object> _storage = new Dictionary<string, object>();

		public bool HasKey(string key)
		{
			return _storage.ContainsKey(key);
		}

		public void DeleteKey(string key)
		{
			_storage.Remove(key);
		}

		public void DeleteAll()
		{
			_storage.Clear();
		}

		public void Save()
		{
			// No-op for in-memory storage
		}

		public bool GetBool(string key, bool defaultValue = false)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				if (value is bool boolValue)
				{
					return boolValue;
				}
				if (value is int intValue)
				{
					return intValue != 0;
				}
			}
			return defaultValue;
		}

		public float GetFloat(string key, float defaultValue = 0)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				if (value is float floatValue)
				{
					return floatValue;
				}
			}
			return defaultValue;
		}

		public int GetInt(string key, int defaultValue = 0)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				if (value is int intValue)
				{
					return intValue;
				}
			}
			return defaultValue;
		}

		public string GetString(string key, string defaultValue = null)
		{
			if (_storage.TryGetValue(key, out var value))
			{
				return value?.ToString() ?? defaultValue;
			}
			return defaultValue;
		}

		public void SetBool(string key, bool value)
		{
			_storage[key] = value;
		}

		public void SetFloat(string key, float value)
		{
			_storage[key] = value;
		}

		public void SetInt(string key, int value)
		{
			_storage[key] = value;
		}

		public void SetString(string key, string value)
		{
			_storage[key] = value ?? string.Empty;
		}
	}
}
