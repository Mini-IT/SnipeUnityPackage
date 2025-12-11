using System;
using System.Collections.Generic;
using System.Linq;
using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	public class PlayerPrefsStringListHelper
	{
		private const string SEPARATOR = ",";
		private readonly ISharedPrefs _sharedPrefs;

		public PlayerPrefsStringListHelper(ISharedPrefs sharedPrefs)
		{
			_sharedPrefs = sharedPrefs;
		}

		public List<string> GetList(string key)
		{
			string value = _sharedPrefs.GetString(key, string.Empty);
			if (string.IsNullOrEmpty(value))
			{
				return new List<string>();
			}

			return value.Split(SEPARATOR[0]).Where(s => !string.IsNullOrEmpty(s)).ToList();
		}

		public void SetList(string key, IList<string> list)
		{
			if (list == null || list.Count == 0)
			{
				_sharedPrefs.DeleteKey(key);
				return;
			}

			string value = string.Join(SEPARATOR, list);
			_sharedPrefs.SetString(key, value);
		}

		public void Add(string key, string item)
		{
			if (string.IsNullOrEmpty(item))
			{
				return;
			}

			var list = GetList(key);
			if (!list.Contains(item))
			{
				list.Add(item);
				SetList(key, list);
			}
		}

		public void Remove(string key, string item)
		{
			if (string.IsNullOrEmpty(item))
			{
				return;
			}

			var list = GetList(key);
			if (list.Remove(item))
			{
				SetList(key, list);
			}
		}

		public void Clear(string key)
		{
			_sharedPrefs.DeleteKey(key);
		}

		public bool Contains(string key, string item)
		{
			var list = GetList(key);
			return list.Contains(item);
		}
	}

	public class PlayerPrefsTypeHelper
	{
		private readonly ISharedPrefs _sharedPrefs;

		public PlayerPrefsTypeHelper(ISharedPrefs sharedPrefs)
		{
			_sharedPrefs = sharedPrefs;
		}

		public T GetPrefsValue<T>(string key)
		{
			if (typeof(T) == typeof(int))
			{
				return (T)(object)_sharedPrefs.GetInt(key, 0);
			}
			else if (typeof(T) == typeof(float))
			{
				return (T)(object)_sharedPrefs.GetFloat(key, 0f);
			}
			else if (typeof(T) == typeof(bool))
			{
				return (T)(object)(_sharedPrefs.GetInt(key, 0) == 1);
			}
			else if (typeof(T) == typeof(string))
			{
				return (T)(object)_sharedPrefs.GetString(key, "");
			}

			return default(T);
		}

		public T GetPrefsValue<T>(string key, T defaultValue)
		{
			if (typeof(T) == typeof(int))
			{
				return (T)(object)_sharedPrefs.GetInt(key, Convert.ToInt32(defaultValue));
			}
			else if (typeof(T) == typeof(float))
			{
				return (T)(object)_sharedPrefs.GetFloat(key, Convert.ToSingle(defaultValue));
			}
			else if (typeof(T) == typeof(bool))
			{
				return (T)(object)(_sharedPrefs.GetInt(key, Convert.ToInt32(defaultValue)) == 1);
			}
			else if (typeof(T) == typeof(string))
			{
				return (T)(object)_sharedPrefs.GetString(key, defaultValue?.ToString() ?? string.Empty);
			}

			return defaultValue;
		}

		public void SetLocalValue(string key, object value)
		{
			if (value is int intValue)
			{
				_sharedPrefs.SetInt(key, intValue);
			}
			else if (value is float floatValue)
			{
				_sharedPrefs.SetFloat(key, floatValue);
			}
			else if (value is bool boolValue)
			{
				_sharedPrefs.SetInt(key, boolValue ? 1 : 0);
			}
			else if (value is string stringValue)
			{
				_sharedPrefs.SetString(key, stringValue);
			}
		}
	}
}

