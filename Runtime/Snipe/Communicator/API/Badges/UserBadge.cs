
namespace MiniIT.Snipe.Api
{
	[System.Serializable]
	public class UserBadge : ISnipeObjectConvertable
	{
		public int id;
		public string stringID;
		public int level;
		public int count;
		public int total;
		public float condValue;

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
			this.count = data.SafeGetValue<int>("count");
			this.total = data.SafeGetValue<int>("total");
			this.condValue = data.SafeGetValue<float>("condValue");
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
				["count"] = this.count,
				["total"] = this.total,
				["condValue"] = this.condValue,
			};
		}
	}
}
