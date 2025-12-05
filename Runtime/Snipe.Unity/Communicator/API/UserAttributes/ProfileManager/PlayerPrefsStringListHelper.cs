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

		public void SetList(string key, List<string> list)
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
}

