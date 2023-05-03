using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiUserAttributes
	{
		private readonly Dictionary<string, SnipeApiUserAttribute> _attributes;
		private readonly object _attributesLock = new object();
		
		public SnipeApiUserAttributes(AbstractSnipeApiService snipeApiService)
		{
			_attributes = new Dictionary<string, SnipeApiUserAttribute>();

			snipeApiService.SubscribeOnMessageReceived(OnMessageReceived);
		}
		
		protected AttrType RegisterAttribute<AttrType>(AttrType attr) where AttrType : SnipeApiUserAttribute
		{
			lock (_attributesLock)
			{
				_attributes[attr.Key] = attr;
			}
			return attr;
		}

		private void OnMessageReceived(string message_type, string error_code, SnipeObject data, int request_id)
		{
			switch (message_type)
			{
				case "attr.getAll":
					UpdateValues(data["data"]);
					break;

				case "attr.changed":
					UpdateValues(data["list"]);
					break;
			}
		}

		private void UpdateValues(object rawList)
		{
			if (rawList is IList list)
			{
				lock (_attributesLock)
				{
					foreach (SnipeObject o in list)
					{
						string key = o.SafeGetString("key");
						if (_attributes.TryGetValue(key, out SnipeApiUserAttribute attr))
						{
							attr.SetValue(o["val"]);
						}
					}
				}
			}
		}
	}

}
