
using System.Collections.Generic;

namespace MiniIT.Snipe
{

	public class UnauthorizedUserAttributeGetter
	{
		public delegate void GetUserAttributeCallback(string error_code, string user_name, string key, object value);

		private readonly ISnipeCommunicator _communicator;

		public UnauthorizedUserAttributeGetter(ISnipeCommunicator communicator)
		{
			_communicator = communicator;
		}

		public void GetUserAttribute(string provider_id, string user_id, string key, GetUserAttributeCallback callback)
		{
			if (_communicator == null)
			{
				callback?.Invoke("error", "", key, null);
				return;
			}

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.AUTH_ATTR_GET).Request(
				new Dictionary<string, object>()
				{
					["provider"] = provider_id,
					["login"] = user_id,
					["key"] = key,
				},
				(error_code, response) =>
				{
					if (callback != null)
					{
						if (response != null)
						{
							callback.Invoke(error_code, response?.SafeGetString("name"), response?.SafeGetString("key"), response?["val"]);
						}
						else
						{
							callback.Invoke("error", "", key, null);
						}
					}
				});
		}
	}
}
