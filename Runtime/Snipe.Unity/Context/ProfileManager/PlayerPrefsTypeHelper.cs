using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MiniIT.Storage;

namespace MiniIT.Snipe.Api
{
	public class PlayerPrefsTypeHelper
	{
		private const char LIST_SEPARATOR = ';';
		private static readonly NumberFormatInfo InvariantNumberFormat = NumberFormatInfo.InvariantInfo;
		private readonly ISharedPrefs _sharedPrefs;

		public PlayerPrefsTypeHelper(ISharedPrefs sharedPrefs)
		{
			_sharedPrefs = sharedPrefs;
		}

		public T GetPrefsValue<T>(string key)
		{
			Type type = typeof(T);
			if (type == typeof(int) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(long) || type == typeof(ulong))
			{
				return (T)(object)_sharedPrefs.GetInt(key, 0);
			}
			else if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
			{
				return (T)(object)_sharedPrefs.GetFloat(key, 0f);
			}
			else if (type == typeof(bool))
			{
				return (T)(object)(_sharedPrefs.GetInt(key, 0) == 1);
			}
			else if (type == typeof(string))
			{
				return (T)(object)_sharedPrefs.GetString(key, "");
			}
			else if (type == typeof(List<int>))
			{
				return (T)(object)ParseIntList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<byte>))
			{
				return (T)(object)ParseByteList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<short>))
			{
				return (T)(object)ParseShortList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<ushort>))
			{
				return (T)(object)ParseUShortList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<long>))
			{
				return (T)(object)ParseLongList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<ulong>))
			{
				return (T)(object)ParseULongList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<float>))
			{
				return (T)(object)ParseFloatList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<double>))
			{
				return (T)(object)ParseDoubleList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<decimal>))
			{
				return (T)(object)ParseDecimalList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<bool>))
			{
				return (T)(object)ParseBoolList(_sharedPrefs.GetString(key, ""));
			}
			else if (type == typeof(List<string>))
			{
				return (T)(object)ParseStringList(_sharedPrefs.GetString(key, ""));
			}

			return default(T);
		}

		public T GetPrefsValue<T>(string key, T defaultValue)
		{
			Type type = typeof(T);
			if (type == typeof(int) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(long) || type == typeof(ulong))
			{
				return (T)(object)_sharedPrefs.GetInt(key, Convert.ToInt32(defaultValue));
			}
			else if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
			{
				return (T)(object)_sharedPrefs.GetFloat(key, Convert.ToSingle(defaultValue));
			}
			else if (type == typeof(bool))
			{
				return (T)(object)(_sharedPrefs.GetInt(key, Convert.ToInt32(defaultValue)) == 1);
			}
			else if (type == typeof(string))
			{
				return (T)(object)_sharedPrefs.GetString(key, defaultValue?.ToString() ?? string.Empty);
			}
			else if (type == typeof(List<int>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseIntList(stored);
			}
			else if (type == typeof(List<byte>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseByteList(stored);
			}
			else if (type == typeof(List<short>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseShortList(stored);
			}
			else if (type == typeof(List<ushort>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseUShortList(stored);
			}
			else if (type == typeof(List<long>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseLongList(stored);
			}
			else if (type == typeof(List<ulong>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseULongList(stored);
			}
			else if (type == typeof(List<float>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseFloatList(stored);
			}
			else if (type == typeof(List<double>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseDoubleList(stored);
			}
			else if (type == typeof(List<decimal>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseDecimalList(stored);
			}
			else if (type == typeof(List<bool>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseBoolList(stored);
			}
			else if (type == typeof(List<string>))
			{
				var stored = _sharedPrefs.GetString(key, "");
				return string.IsNullOrEmpty(stored) ? defaultValue : (T)(object)ParseStringList(stored);
			}

			return defaultValue;
		}

		public void SetLocalValue(string key, object value)
		{
			switch (value)
			{
				case int intValue:
					_sharedPrefs.SetInt(key, intValue);
					break;
				case byte intValue:
					_sharedPrefs.SetInt(key, intValue);
					break;
				case short intValue:
					_sharedPrefs.SetInt(key, intValue);
					break;
				case ushort intValue:
					_sharedPrefs.SetInt(key, intValue);
					break;
				case long intValue:
					_sharedPrefs.SetInt(key, (int)intValue);
					break;
				case ulong intValue:
					_sharedPrefs.SetInt(key, (int)intValue);
					break;
				case float floatValue:
					_sharedPrefs.SetFloat(key, floatValue);
					break;
				case double floatValue:
					_sharedPrefs.SetFloat(key, (float)floatValue);
					break;
				case bool boolValue:
					_sharedPrefs.SetInt(key, boolValue ? 1 : 0);
					break;
				case string stringValue:
					_sharedPrefs.SetString(key, stringValue);
					break;
				case List<int> intList:
					_sharedPrefs.SetString(key, SerializeIntList(intList));
					break;
				case List<byte> byteList:
					_sharedPrefs.SetString(key, SerializeByteList(byteList));
					break;
				case List<short> shortList:
					_sharedPrefs.SetString(key, SerializeShortList(shortList));
					break;
				case List<ushort> ushortList:
					_sharedPrefs.SetString(key, SerializeUShortList(ushortList));
					break;
				case List<long> longList:
					_sharedPrefs.SetString(key, SerializeLongList(longList));
					break;
				case List<ulong> ulongList:
					_sharedPrefs.SetString(key, SerializeULongList(ulongList));
					break;
				case List<float> floatList:
					_sharedPrefs.SetString(key, SerializeFloatList(floatList));
					break;
				case List<double> doubleList:
					_sharedPrefs.SetString(key, SerializeDoubleList(doubleList));
					break;
				case List<decimal> decimalList:
					_sharedPrefs.SetString(key, SerializeDecimalList(decimalList));
					break;
				case List<bool> boolList:
					_sharedPrefs.SetString(key, SerializeBoolList(boolList));
					break;
				case List<string> stringList:
					_sharedPrefs.SetString(key, SerializeStringList(stringList));
					break;
			}
		}

		private List<T> ParseNumericList<T>(string value, Func<string, T> parser)
		{
			if (string.IsNullOrEmpty(value))
			{
				return new List<T>();
			}

			var parts = SplitQuotedStrings(value);
			var result = new List<T>(parts.Count);
			foreach (var part in parts)
			{
				try
				{
					result.Add(parser(UnquoteString(part)));
				}
				catch
				{
					// Skip invalid values
				}
			}
			return result;
		}

		private List<int> ParseIntList(string value) => ParseNumericList(value, s => int.Parse(s, NumberStyles.Integer, InvariantNumberFormat));
		private List<byte> ParseByteList(string value) => ParseNumericList(value, s => byte.Parse(s, NumberStyles.Integer, InvariantNumberFormat));
		private List<short> ParseShortList(string value) => ParseNumericList(value, s => short.Parse(s, NumberStyles.Integer, InvariantNumberFormat));
		private List<ushort> ParseUShortList(string value) => ParseNumericList(value, s => ushort.Parse(s, NumberStyles.Integer, InvariantNumberFormat));
		private List<long> ParseLongList(string value) => ParseNumericList(value, s => long.Parse(s, NumberStyles.Integer, InvariantNumberFormat));
		private List<ulong> ParseULongList(string value) => ParseNumericList(value, s => ulong.Parse(s, NumberStyles.Integer, InvariantNumberFormat));
		private List<float> ParseFloatList(string value) => ParseNumericList(value, s => float.Parse(s, NumberStyles.Float, InvariantNumberFormat));
		private List<double> ParseDoubleList(string value) => ParseNumericList(value, s => double.Parse(s, NumberStyles.Float, InvariantNumberFormat));
		private List<decimal> ParseDecimalList(string value) => ParseNumericList(value, s => decimal.Parse(s, NumberStyles.Float, InvariantNumberFormat));

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

		private string SerializeNumericList<T>(List<T> list) where T : IFormattable
		{
			if (list == null || list.Count == 0)
			{
				return string.Empty;
			}
			return string.Join(LIST_SEPARATOR, list.Select(item => QuoteString(item.ToString(null, InvariantNumberFormat))));
		}

		private string SerializeIntList(List<int> list) => SerializeNumericList(list);
		private string SerializeByteList(List<byte> list) => SerializeNumericList(list);
		private string SerializeShortList(List<short> list) => SerializeNumericList(list);
		private string SerializeUShortList(List<ushort> list) => SerializeNumericList(list);
		private string SerializeLongList(List<long> list) => SerializeNumericList(list);
		private string SerializeULongList(List<ulong> list) => SerializeNumericList(list);
		private string SerializeFloatList(List<float> list) => SerializeNumericList(list);
		private string SerializeDoubleList(List<double> list) => SerializeNumericList(list);
		private string SerializeDecimalList(List<decimal> list) => SerializeNumericList(list);

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
