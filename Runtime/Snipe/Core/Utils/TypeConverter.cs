using System;
using System.Collections;
using System.Globalization;

namespace MiniIT
{
	public static class TypeConverter
	{
		public static T Convert<T>(object val)
		{
			if (val == null)
			{
				if (typeof(T).IsValueType)
				{
					return (T)System.Convert.ChangeType(0, typeof(T), CultureInfo.InvariantCulture);
				}
				else
				{
					return default;
				}
			}
			else
			{
				Type resultType = typeof(T);
				if (resultType == typeof(object))
				{
					return (T)val;
				}
				else if (val is ICollection collection)
				{
					return ConvertToList<T>(collection);
				}
				else
				{
					return (T)System.Convert.ChangeType(val, resultType, CultureInfo.InvariantCulture);
				}
			}
		}

		public static TList ConvertToList<TList>(ICollection collection)
		{
			var list = (IList)Activator.CreateInstance<TList>();
			if (list == null)
			{
				return default;
			}

			foreach (var item in collection)
			{
				list.Add(item);
			}

			return (TList)list;
		}
	}
}
