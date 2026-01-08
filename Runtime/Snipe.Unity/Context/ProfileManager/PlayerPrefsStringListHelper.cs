using System;
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

	public class PlayerPrefsTypeHelper
	{
		private const char LIST_SEPARATOR = ';';
		private static readonly System.Globalization.NumberFormatInfo InvariantNumberFormat = System.Globalization.NumberFormatInfo.InvariantInfo;
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
			else if (typeof(T) == typeof(List<int>))
			{
				return (T)(object)ParseIntList(_sharedPrefs.GetString(key, ""));
			}
			else if (typeof(T) == typeof(List<float>))
			{
				return (T)(object)ParseFloatList(_sharedPrefs.GetString(key, ""));
			}
			else if (typeof(T) == typeof(List<bool>))
			{
				return (T)(object)ParseBoolList(_sharedPrefs.GetString(key, ""));
			}
			else if (typeof(T) == typeof(List<string>))
			{
				return (T)(object)ParseStringList(_sharedPrefs.GetString(key, ""));
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
			else if (typeof(T) == typeof(List<int>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseIntList(stored);
			}
			else if (typeof(T) == typeof(List<float>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseFloatList(stored);
			}
			else if (typeof(T) == typeof(List<bool>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseBoolList(stored);
			}
			else if (typeof(T) == typeof(List<string>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseStringList(stored);
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
			else if (value is List<int> intList)
			{
				_sharedPrefs.SetString(key, SerializeIntList(intList));
			}
			else if (value is List<float> floatList)
			{
				_sharedPrefs.SetString(key, SerializeFloatList(floatList));
			}
			else if (value is List<bool> boolList)
			{
				_sharedPrefs.SetString(key, SerializeBoolList(boolList));
			}
			else if (value is List<string> stringList)
			{
				_sharedPrefs.SetString(key, SerializeStringList(stringList));
			}
		}

		private List<int> ParseIntList(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return new List<int>();
			}

			var parts = SplitQuotedStrings(value);
			var result = new List<int>(parts.Count);
			foreach (var part in parts)
			{
				if (int.TryParse(UnquoteString(part), System.Globalization.NumberStyles.Integer, InvariantNumberFormat, out var num))
				{
					result.Add(num);
				}
			}
			return result;
		}

		private List<float> ParseFloatList(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return new List<float>();
			}

			var parts = SplitQuotedStrings(value);
			var result = new List<float>(parts.Count);
			foreach (var part in parts)
			{
				if (float.TryParse(UnquoteString(part), System.Globalization.NumberStyles.Float, InvariantNumberFormat, out var num))
				{
					result.Add(num);
				}
			}
			return result;
		}

		private List<bool> ParseBoolList(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return new List<bool>();
			}

			var parts = SplitQuotedStrings(value);
			var result = new List<bool>(parts.Count);
			foreach (var part in parts)
			{
				var unquoted = UnquoteString(part);
				if (bool.TryParse(unquoted, out var boolValue))
				{
					result.Add(boolValue);
				}
				else if (int.TryParse(unquoted, out var num))
				{
					result.Add(num != 0);
				}
			}
			return result;
		}

		private List<string> ParseStringList(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return new List<string>();
			}

			var parts = SplitQuotedStrings(value);
			return parts.Select(UnquoteString).ToList();
		}

		private string SerializeIntList(List<int> list)
		{
			if (list == null || list.Count == 0)
			{
				return string.Empty;
			}
			return string.Join(LIST_SEPARATOR, list.Select(i => QuoteString(i.ToString(InvariantNumberFormat))));
		}

		private string SerializeFloatList(List<float> list)
		{
			if (list == null || list.Count == 0)
			{
				return string.Empty;
			}
			return string.Join(LIST_SEPARATOR, list.Select(f => QuoteString(f.ToString(InvariantNumberFormat))));
		}

		private string SerializeBoolList(List<bool> list)
		{
			if (list == null || list.Count == 0)
			{
				return string.Empty;
			}
			return string.Join(LIST_SEPARATOR, list.Select(b => QuoteString(b.ToString())));
		}

		private string SerializeStringList(List<string> list)
		{
			if (list == null || list.Count == 0)
			{
				return string.Empty;
			}
			return string.Join(LIST_SEPARATOR, list.Select(QuoteString));
		}

		private static string QuoteString(string input)
		{
			if (input == null)
			{
				return "\"\"";
			}

			// Escape quotes and backslashes
			var escaped = input.Replace("\\", "\\\\").Replace("\"", "\\\"");
			return "\"" + escaped + "\"";
		}

		private static string UnquoteString(string input)
		{
			if (string.IsNullOrEmpty(input) || input.Length < 2 || !input.StartsWith("\"") || !input.EndsWith("\""))
			{
				return input;
			}

			var content = input.Substring(1, input.Length - 2);
			// Unescape quotes and backslashes
			return content.Replace("\\\"", "\"").Replace("\\\\", "\\");
		}

		private static List<string> SplitQuotedStrings(string input)
		{
			var result = new List<string>();
			var current = new System.Text.StringBuilder();
			bool inQuotes = false;
			bool escaped = false;

			for (int i = 0; i < input.Length; i++)
			{
				char c = input[i];

				if (escaped)
				{
					current.Append(c);
					escaped = false;
				}
				else if (c == '\\')
				{
					escaped = true;
				}
				else if (c == '"')
				{
					inQuotes = !inQuotes;
					current.Append(c);
				}
				else if (c == LIST_SEPARATOR && !inQuotes)
				{
					if (current.Length > 0)
					{
						result.Add(current.ToString());
						current.Clear();
					}
				}
				else
				{
					current.Append(c);
				}
			}

			// Add the last part
			if (current.Length > 0)
			{
				result.Add(current.ToString());
			}

			return result;
		}
	}
}

