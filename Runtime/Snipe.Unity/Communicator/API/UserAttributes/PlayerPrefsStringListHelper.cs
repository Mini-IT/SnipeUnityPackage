using System.Collections.Generic;
using System.Linq;

#if MINIIT_SHARED_PREFS
using MiniIT.Storage;
#else
using SharedPrefs = UnityEngine.PlayerPrefs;
#endif

namespace MiniIT.Snipe.Api
{
	public static class PlayerPrefsStringListHelper
	{
		private const string SEPARATOR = ",";

		public static List<string> GetList(string key)
		{
			string value = SharedPrefs.GetString(key, string.Empty);
			if (string.IsNullOrEmpty(value))
			{
				return new List<string>();
			}

			return value.Split(SEPARATOR[0]).Where(s => !string.IsNullOrEmpty(s)).ToList();
		}

		public static void SetList(string key, List<string> list)
		{
			if (list == null || list.Count == 0)
			{
				SharedPrefs.DeleteKey(key);
				return;
			}

			string value = string.Join(SEPARATOR, list);
			SharedPrefs.SetString(key, value);
		}

		public static void Add(string key, string item)
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

		public static void Remove(string key, string item)
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

		public static void Clear(string key)
		{
			SharedPrefs.DeleteKey(key);
		}

		public static bool Contains(string key, string item)
		{
			var list = GetList(key);
			return list.Contains(item);
		}
	}
}

