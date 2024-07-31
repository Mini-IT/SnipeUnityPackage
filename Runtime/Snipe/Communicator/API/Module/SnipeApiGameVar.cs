namespace MiniIT.Snipe.Api
{
	public abstract class SnipeApiGameVar
	{
		public string Name { get; }

		protected SnipeApiGameVar(string name) => Name = name;
	}

	public class SnipeApiGameVar<TValue> : SnipeApiGameVar
	{
		public TValue Value { get; set; }

		public SnipeApiGameVar(string name, TValue value = default)
			: base(name)
		{
			Value = value;
		}

		public static implicit operator TValue(SnipeApiGameVar<TValue> item)
		{
			return item.Value;
		}
	}
}
