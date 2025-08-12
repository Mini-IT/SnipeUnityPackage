using System;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public abstract class AbstractSnipeApiRoomModule : SnipeApiModule
	{
		public event Action<int> Joined;
		public event Action<string> BroadcastReceived;

		public AbstractSnipeApiRoomModule(AbstractSnipeApiService snipeApiService)
			: base(snipeApiService)
		{
			SubscribeOnMessageReceived(OnMessageReceived);
		}

		private void OnMessageReceived(string messageType, string errorCode, IDictionary<string, object> data, int requestId)
		{
			switch (messageType)
			{
				case "room.join":
					if (errorCode == SnipeErrorCodes.OK)
					{
						int roomId = data.SafeGetValue<int>("roomID");
						if (roomId != 0)
						{
							Joined?.Invoke(roomId);
						}
					}
					break;

				case "room.broadcast":
				{
					if (errorCode == SnipeErrorCodes.OK)
					{
						string msg = data.SafeGetString("msg");
						if (!string.IsNullOrEmpty(msg))
						{
							BroadcastReceived?.Invoke(errorCode);
						}
					}
					break;
				}
			}
		}
	}
}
