using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class UserAttributeGetter
	{
		public delegate void GetUserAttributeCallback(string errorCode, string key, object value);
		public delegate void GetUserAttributesCallback(string errorCode, IDictionary<string, object> values);

		private readonly SnipeCommunicator _communicator;

		public UserAttributeGetter(SnipeCommunicator communicator)
		{
			_communicator = communicator;
		}

		public void GetUserAttribute(int userId, string key, GetUserAttributeCallback callback)
		{
			GetUserAttribute(new Dictionary<string, object>()
			{
				["userID"] = userId,
			},
			key,
			callback);
		}

		public void GetUserAttribute(string providerId, string loginId, string key, GetUserAttributeCallback callback)
		{
			GetUserAttribute(new Dictionary<string, object>()
			{
				["provider"] = providerId,
				["login"] = loginId,
			},
			key,
			callback);
		}

		public void GetUserAttributes(int userId, string[] keys, GetUserAttributesCallback callback)
		{
			GetUserAttributes(
				new Dictionary<string, object>()
				{
					["userID"] = userId,
					["keys"] = keys,
				},
				callback);
		}

		public void GetUserAttributes(string providerId, string loginId, string[] keys, GetUserAttributesCallback callback)
		{
			GetUserAttributes(
				new Dictionary<string, object>()
				{
					["provider"] = providerId,
					["login"] = loginId,
					["keys"] = keys,
				},
				callback);
		}

		private void GetUserAttribute(IDictionary<string, object> requestData, string key, GetUserAttributeCallback callback)
		{
			if (_communicator == null)
			{
				callback?.Invoke("error", key, null);
				return;
			}

			requestData["key"] = key;

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.ATTR_GET).Request(
				requestData,
				(error_code, response) =>
				{
					if (callback == null)
						return;

					if (response != null)
					{
						callback.Invoke(error_code, response.SafeGetString("key"), response["val"]);
					}
					else
					{
						callback.Invoke("error", key, null);
					}
				});
		}

		private void GetUserAttributes(IDictionary<string, object> requestData, GetUserAttributesCallback callback)
		{
			if (_communicator == null)
			{
				callback?.Invoke("error", null);
				return;
			}

			new UnauthorizedRequest(_communicator, SnipeMessageTypes.ATTR_GET_MULTI).Request(
				requestData,
				(error_code, response) =>
				{
					if (callback == null)
						return;

					if (response != null && response.TryGetValue("data", out IList items))
					{
						IDictionary<string, object> result = new Dictionary<string, object>();
						foreach (var listItem in items)
						{
							if (listItem is IDictionary<string, object> item)
							{
								result.Add(item.SafeGetString("key"), item["val"]);
							}
						}
						callback.Invoke(error_code, result);
					}
					else
					{
						callback.Invoke("error", null);
					}
				});
		}
	}
}
