#if UNITY_WEBGL && !UNITY_EDITOR

using System.Collections.Generic;

namespace MiniIT.Snipe.SharedPrefs.Unity
{
	public class WebGLSharedPrefs : ISharedPrefs
	{
		public void DeleteAll()
		{
			Dictionary<string, string> all = HttpCookie.GetAllCookies();
			foreach (var pair in all)
			{
				HttpCookie.RemoveCookie(pair.Key);
			}
		}

		public void DeleteKey(string key)
		{
			HttpCookie.RemoveCookie(key);
		}

		public bool GetBool(string key, bool defaultValue = default)
		{
			string value = HttpCookie.GetCookie(key);
			if (int.TryParse(value, out int val))
			{
				return (val != 0);
			}
			return defaultValue;
		}

		public float GetFloat(string key, float defaultValue = default)
		{
			string value = HttpCookie.GetCookie(key);
			if (float.TryParse(value, out float val))
			{
				return val;
			}
			return defaultValue;
		}

		public int GetInt(string key, int defaultValue = default)
		{
			string value = HttpCookie.GetCookie(key);
			if (int.TryParse(value, out int val))
			{
				return val;
			}
			return defaultValue;
		}

		public string GetString(string key, string defaultValue = default)
		{
			string value = HttpCookie.GetCookie(key);
			if (string.IsNullOrEmpty(value))
			{
				return defaultValue;
			}
			return value;
		}

		public bool HasKey(string key)
		{
			Dictionary<string, string> all = HttpCookie.GetAllCookies();
			return all.ContainsKey(key);
		}

		public void Save()
		{
		}

		public void SetBool(string key, bool value)
		{
			SetInt(key, value ? 1 : 0);
		}

		public void SetFloat(string key, float value)
		{
			HttpCookie.SetCookie(key, value.ToString());
		}

		public void SetInt(string key, int value)
		{
			HttpCookie.SetCookie(key, value.ToString());
		}

		public void SetString(string key, string value)
		{
			HttpCookie.SetCookie(key, value);
		}
	}
}

#endif
