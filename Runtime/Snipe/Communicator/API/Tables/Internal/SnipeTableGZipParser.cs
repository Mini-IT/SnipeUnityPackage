using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using fastJSON;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableGZipParser
	{
		public static async Task<bool> TryReadAsync(Type wrapperType, IDictionary items, Stream stream)
		{
			try
			{
				await ReadAsync(wrapperType, items, stream);
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		public static async Task ReadAsync(Type wrapperType, IDictionary items, Stream stream)
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json = await reader.ReadToEndAsync();

					ISnipeTableItemsListWrapper listWrapper = Parse(wrapperType, json) as ISnipeTableItemsListWrapper;
					var list = listWrapper?.GetList();
					if (list != null)
					{
						foreach (var item in list)
						{
							if (item is SnipeTableItem sti)
							{
								items[sti.id] = item;
							}
						}
					}

					//this.Loaded = true;
				}
			}
		}

		private static object Parse(Type wrapperType, string json)
		{
			Dictionary<string, object> jsonObject = JSON.ToObject<Dictionary<string, object>>(json);
			return ParseInternal(wrapperType, jsonObject);
		}

		private static object ParseInternal(Type type, Dictionary<string, object> jsonObject)
		{
			object instance = Activator.CreateInstance(type);

			foreach (var kvp in jsonObject)
			{
				PropertyInfo property = type.GetProperty(kvp.Key);
				if (property != null)
				{
					Type propertyType = property.PropertyType;
					object parsedValue = ParseValue(kvp.Value, propertyType);
					property.SetValue(instance, parsedValue);
				}
				else
				{
					FieldInfo field = type.GetField(kvp.Key);
					if (field != null)
					{
						object parsedValue = ParseValue(kvp.Value, field.FieldType);
						field.SetValue(instance, parsedValue);
					}
				}
			}

			return instance;
		}

		private static object ParseValue(object value, Type targetType)
		{
			if (value.GetType() == targetType)
			{
				return value;
			}
			else if (value is Dictionary<string, object> nestedObj)
			{
				return ParseInternal(targetType, nestedObj);
			}
			else if (value is IList<object> itemList)
			{
				if (targetType.IsGenericType)
				{
					Type elementType = targetType.GetGenericArguments()[0];
					IList list = (IList)Activator.CreateInstance(targetType);
					foreach (var item in itemList)
					{
						if (item is Dictionary<string, object> nestedItemObj)
						{
							object nestedInstance = ParseInternal(elementType, nestedItemObj);
							list.Add(nestedInstance);
						}
					}
					return list;
				}
				else if (targetType.IsArray)
				{
					var elementType = targetType.GetElementType();
					var array = Array.CreateInstance(elementType, itemList.Count);

					for (int i = 0; i < itemList.Count; i++)
					{
						array.SetValue(Convert.ChangeType(itemList[i], elementType), i);
					}

					return array;
				}
				// else // ????
			}

			return Convert.ChangeType(value, targetType);
		}
	}
}
