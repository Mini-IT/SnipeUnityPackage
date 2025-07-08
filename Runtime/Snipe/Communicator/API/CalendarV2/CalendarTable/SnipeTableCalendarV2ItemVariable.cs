namespace MiniIT.Snipe.Api
{
	public class SnipeTableCalendarV2ItemVariable
	{
		public string StringID { get; set; }
		public string Type { get; set; }
		public string Value { get; set; }
		public string ValueDev { get; set; }

		public T GetValue<T>(bool dev = false)
		{
			switch (Type)
			{
				case "string":
					if (dev && !string.IsNullOrEmpty(ValueDev))
					{
						return (T)System.Convert.ChangeType(ValueDev, typeof(T));
					}
					else
					{
						return (T)System.Convert.ChangeType(Value, typeof(T));
					}
				case "int":
					if (dev && !string.IsNullOrEmpty(ValueDev))
					{
						return (T)System.Convert.ChangeType(ValueDev, typeof(T));
					}
					else
					{
						return (T)System.Convert.ChangeType(Value, typeof(T));
					}
				case "float":
					if (dev && !string.IsNullOrEmpty(ValueDev))
					{
						return (T)System.Convert.ChangeType(ValueDev, typeof(T));
					}
					else
					{
						return (T)System.Convert.ChangeType(Value, typeof(T));
					}
				case "bool":
					if (dev && !string.IsNullOrEmpty(ValueDev))
					{
						return (T)System.Convert.ChangeType(ValueDev, typeof(T));
					}
					else
					{
						return (T)System.Convert.ChangeType(Value, typeof(T));
					}
				default:
					{
						return default;
					}
			}
		}
	}
}
