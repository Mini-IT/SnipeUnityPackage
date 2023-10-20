
namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class UserBadge : ISnipeObjectConvertable
	{
		public int id;
		public string stringID;
		public int level;
		public int currentValue;
		public int startValue;
		public int targetValue;

		public UserBadge(SnipeObject data = null)
		{
			if (data == null)
				return;

			Update(data);
		}

		internal void Update(SnipeObject data)
		{
			this.id = data.SafeGetValue<int>("id");
			this.stringID = data.SafeGetString("stringID");
			this.level = data.SafeGetValue<int>("level");
			this.currentValue = data.SafeGetValue<int>("progress");
			this.startValue = data.SafeGetValue<int>("start");
			this.targetValue = data.SafeGetValue<int>("target");
		}

		public static implicit operator UserBadge(SnipeObject data)
		{
			return new UserBadge(data);
		}

		public SnipeObject ConvertToSnipeObject()
		{
			return new SnipeObject()
			{
				["id"] = this.id,
				["stringID"] = this.stringID ?? "",
				["level"] = this.level,
				["progress"] = this.currentValue,
				["start"] = this.startValue,
				["target"] = this.targetValue,
			};
		}
	}
}
