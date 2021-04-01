using MiniIT;
using System;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeTableItem
	{
		public int id;
	}
	
	public interface ISnipeTableItemsListWrapper<ItemType>
	{
		List<ItemType> list { get; set; }
	}
	
	public class SnipeTableAttrsItem : SnipeTableItem
	{
		public object attrs { get; set; }
		
		public T GetAttr<T>(string key) where T : new()
		{
			if (attrs == null)
				return default;
			
			T result = default;

			if (attrs is IDictionary<string, object> attrs_dict)
			{
				if (attrs_dict.TryGetValue(key, out var value))
				{
					try
					{
						result = (T)value;
					}
					catch (InvalidCastException)
					{
						try
						{
							result = (T)Convert.ChangeType(value, typeof(T));
						}
						catch (Exception)
						{
							result = default;
						}
					}
					catch (NullReferenceException) // field exists but value is null
					{
						result = default;
					}
				}
			}
			else
			{
				var attrs_type = attrs.GetType();
				try
				{
					var field = attrs_type.GetField(key);
					if (field != null)
					{
						result = (T)Convert.ChangeType(field.GetValue(attrs), typeof(T));
					}
					else
					{
						var prop = attrs_type.GetProperty(key);
						if (prop != null)
						{
							result = (T)Convert.ChangeType(prop.GetValue(attrs), typeof(T));
						}
					}
				}
				catch (Exception)
				{
					result = default;
				}
			}
			
			return result;
		}
	}
}