using System;
using System.Collections.Generic;
using System.Globalization;

namespace MiniIT
{
	public static class DictionaryExtensions
	{
		public static bool TryGetValue<T>(this IDictionary<string, object> dictionary, string key, out T value)
		{
			if (dictionary.TryGetValue(key, out var result))
			{
				try
				{
					value = (T)result;
					return true;
				}
				catch (InvalidCastException)
				{
					try
					{
						value = (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
						return true;
					}
					catch (Exception)
					{
					}
				}
				catch (NullReferenceException) // field exists but res is null
				{
				}
			}

			value = default;
			return false;
		}

		public static T SafeGetValue<T>(this IDictionary<string, object> dictionary, string key, T defaultValue = default)
		{
			return TryGetValue<T>(dictionary, key, out var result) ? result : defaultValue;
		}

		public static string SafeGetString(this IDictionary<string, object> dictionary, string key, string defaultValue = "")
		{
			return TryGetValue(dictionary, key, out object value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : defaultValue;
		}

		public static bool ContentEquals(this IDictionary<string, object> first, IDictionary<string, object> second)
		{
			// If both dictionaries are the same instance, they are equal
			if (ReferenceEquals(first, second))
			{
				return true;
			}

			// If one of the dictionaries is null, they are not equal
			if (first == null || second == null)
			{
				return false;
			}

			// If the dictionaries have different sizes, they are not equal
			if (first.Count != second.Count)
			{
				return false;
			}

			// Iterate over the first dictionary and compare each key and value
			foreach (var kvp in first)
			{
				// Check if the key exists in the second dictionary
				if (!second.TryGetValue(kvp.Key, out object secondValue))
					return false;

				// Compare the values for the same key
				if (!Equals(kvp.Value, secondValue))
					return false;
			}

			return true;
		}
	}
}
