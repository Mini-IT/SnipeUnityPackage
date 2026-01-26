using System.Collections.Generic;
using System.Linq;
using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	public class PlayerPrefsStringListHelper
	{
		private const char SEPARATOR = ',';
		private readonly ISharedPrefs _sharedPrefs;
		private readonly string _key;
		private List<string> _values;

		public PlayerPrefsStringListHelper(ISharedPrefs sharedPrefs, string key)
		{
			_sharedPrefs = sharedPrefs;
			_key = key;
			_values = InitList();
		}

		private List<string> InitList()
		{
			string value = _sharedPrefs.GetString(_key, string.Empty);
			if (string.IsNullOrEmpty(value))
			{
				return new List<string>();
			}

			return value.Split(SEPARATOR).Where(s => !string.IsNullOrEmpty(s)).ToList();
		}

		public List<string> GetList() => _values;

		public void SetList(List<string> list)
		{
			_values = list;

			if (list == null || list.Count == 0)
			{
				_sharedPrefs.DeleteKey(_key);
				return;
			}

			string value = string.Join(SEPARATOR, list);
			_sharedPrefs.SetString(_key, value);
		}

		public void Add(string item)
		{
			if (string.IsNullOrEmpty(item))
			{
				return;
			}

			var list = GetList();
			if (!list.Contains(item))
			{
				list.Add(item);
				SetList(list);
			}
		}

		public void Remove(string item)
		{
			if (string.IsNullOrEmpty(item))
			{
				return;
			}

			var list = GetList();
			if (list.Remove(item))
			{
				SetList(list);
			}
		}

		public void Clear()
		{
			_sharedPrefs.DeleteKey(_key);
		}

		public bool Contains(string item)
		{
			var list = GetList();
			return list.Contains(item);
		}
	}
}

