namespace MiniIT.Snipe.Api
{
	public static class ProfileAttributeExtensions
	{
		public static T GetValue<T>(this ProfileAttribute<T> valueSyncer) => valueSyncer.Value;
		public static void SetValue<T>(this ProfileAttribute<T> valueSyncer, T value) => valueSyncer.Value = value;
		public static void Increase(this ProfileAttribute<int> valueSyncer, int amount = 1) => valueSyncer.Value += amount;
		public static void Decrease(this ProfileAttribute<int> valueSyncer, int amount = 1) => valueSyncer.Value -= amount;
	}

	public static class LocalProfileAttributeExtension
	{
		public static T GetValue<T>(this LocalProfileAttribute<T> attr) => attr.Value;
		public static void SetValue<T>(this LocalProfileAttribute<T> attr, T value) => attr.Value = value;
		public static void Increase(this LocalProfileAttribute<int> attr, int amount = 1) => attr.Value += amount;
		public static void Decrease(this LocalProfileAttribute<int> attr, int amount = 1) => attr.Value -= amount;
	}
}
