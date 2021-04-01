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
	
	public class SnipeTableAttrsItem<T> : SnipeTableItem
	{
		public T attrs { get; set; }
	}
	/*
	public class SnipeTableAttrsItem : SnipeTableItem
	{
		public object attrs { get; set; }
		
		public T GetAttr<T>(string key) where T : new()
		{
			DebugLogger.Log($"[SnipeTableAttrsItem] GetAttr {typeof(T)} - {key} - attrs = {attrs}");
			
			if (attrs == null)
				return default;
				
			DebugLogger.Log($"[SnipeTableAttrsItem] {attrs.GetType()}");
			
			T result = default;

			if (attrs is IDictionary<string, object> attrs_dict)
			{
				DebugLogger.Log($"[SnipeTableAttrsItem] attrs is Dictionary");
				
				if (attrs_dict.TryGetValue(key, out var value))
				{
					DebugLogger.Log($"[SnipeTableAttrsItem] Dictionary item value = {value}");
					
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
				DebugLogger.Log($"[SnipeTableAttrsItem] attrs is NOT a Dictionary");
				
				var attrs_type = attrs.GetType();
				try
				{
					var field = attrs_type.GetField(key);
					if (field != null)
					{
						DebugLogger.Log($"[SnipeTableAttrsItem] field");
						result = (T)Convert.ChangeType(field.GetValue(attrs), typeof(T));
					}
					else
					{
						var prop = attrs_type.GetProperty(key);
						if (prop != null)
						{
							DebugLogger.Log($"[SnipeTableAttrsItem] property");
							result = (T)Convert.ChangeType(prop.GetValue(attrs), typeof(T));
						}
					}
				}
				catch (Exception e)
				{
					result = default;
					
					DebugLogger.Log($"[SnipeTableAttrsItem] field/property GetValue exception: {e.StackTrace}");
				}
			}
			
			return result;
		}
	}
	*/
}