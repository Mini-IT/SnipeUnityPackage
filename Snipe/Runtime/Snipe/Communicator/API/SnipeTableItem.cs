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
		public Dictionary<string, object> attrs { get; set; }
		
		public T GetAttr<T>(string key) where T : new()
		{
			if (attrs == null)
				return default;
			
			T result = default;
			
			if (attrs.TryGetValue(key, out var value))
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
			
			return result;
		}
	}
}