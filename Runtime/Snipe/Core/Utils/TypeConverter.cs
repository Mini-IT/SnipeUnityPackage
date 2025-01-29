using System;
using System.Collections;
using System.Globalization;

namespace MiniIT
{
	public static class TypeConverter
	{
		public static T Convert<T>(object value)
		{
			if (value is T t)
			{
				return t;
			}

			if (value == null)
			{
				return default;
			}

			Type resultType = typeof(T);
			if (resultType == typeof(object))
			{
				return (T)value;
			}
			else if (value is ICollection collection && typeof(IList).IsAssignableFrom(resultType))
			{
				return ConvertToList<T>(collection);
			}
			else
			{
				return (T)System.Convert.ChangeType(value, resultType, CultureInfo.InvariantCulture);
			}
		}

		public static TList ConvertToList<TList>(ICollection collection)
		{
			if (collection is TList t)
			{
				return t;
			}

			if (collection == null)
			{
				return default;
			}

			IList list = (IList)Activator.CreateInstance<TList>();
			if (list == null)
			{
				return default;
			}

			Type listType = typeof(TList);
			Type targetType = listType.IsGenericType ? listType.GetGenericArguments()[0] : null;

			foreach (var item in collection)
			{
				if (targetType != null)
					list.Add(System.Convert.ChangeType(item, targetType, CultureInfo.InvariantCulture));
				else
					list.Add(item);
			}

			return (TList)list;
		}
	}
}
