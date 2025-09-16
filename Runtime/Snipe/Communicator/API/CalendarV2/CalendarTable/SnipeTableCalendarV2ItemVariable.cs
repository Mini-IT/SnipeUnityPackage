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
			return ChangeType<T>(dev);
		}

		private T ChangeType<T>(bool dev)
		{
			if (dev && !string.IsNullOrEmpty(ValueDev))
			{
				return (T)System.Convert.ChangeType(ValueDev, typeof(T));
			}

			return (T)System.Convert.ChangeType(Value, typeof(T));
		}
	}
}
