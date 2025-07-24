using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using fastJSON;
using MiniIT.Snipe.Api;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableParser
	{
		public static void Parse(Type wrapperType, IDictionary items, string json)
		{
			Dictionary<string, object> jsonObject = JSON.ToObject<Dictionary<string, object>>(json);
			ISnipeTableItemsListWrapper listWrapper;
			if (wrapperType == typeof(SnipeTableItemsListWrapper<SnipeTableLogicItem>))
			{
				listWrapper = SnipeTableLogicItemsWrapper.FromTableData(jsonObject);
			}
			else if (wrapperType == typeof(SnipeTableItemsListWrapper<SnipeTableCalendarItem>))
			{
				listWrapper = SnipeTableCalendarItemsWrapper.FromTableData(jsonObject);
			}
			else if (wrapperType == typeof(SnipeTableItemsListWrapper<SnipeTableCalendarV2Item>))
			{
				listWrapper = SnipeTableCalendarV2ItemsWrapper.FromTableData(jsonObject);
			}
			else
			{
				listWrapper = ParseInternal(wrapperType, jsonObject) as ISnipeTableItemsListWrapper;
			}
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
			if (targetType == typeof(object))
			{
				return value;
			}

			if (value is Dictionary<string, object> nestedObj)
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
						else
						{
							list.Add(ParseValue(item, elementType));
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
			else if (value.GetType() == targetType)
			{
				return value;
			}

			return Convert.ChangeType(value, targetType);
		}
	}
}
