using System;
using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiUserAttributes : IDisposable
	{
		private readonly Dictionary<string, SnipeApiUserAttribute> _attributes;
		private readonly object _attributesLock = new object();

		public SnipeApiUserAttributes(AbstractSnipeApiService snipeApiService)
		{
			_attributes = new Dictionary<string, SnipeApiUserAttribute>();

			snipeApiService.SubscribeOnMessageReceived(OnMessageReceived);
		}

		// `internal` for being accessable for tests
		protected internal AttrType RegisterAttribute<AttrType>(AttrType attr) where AttrType : SnipeApiUserAttribute
		{
			lock (_attributesLock)
			{
				_attributes[attr.Key] = attr;
			}

			return attr;
		}

		public bool TryGetAttribute<T>(string key, out SnipeApiReadOnlyUserAttribute<T> attr)
		{
			lock (_attributesLock)
			{
				if (_attributes.TryGetValue(key, out SnipeApiUserAttribute baseAttr) &&
				    baseAttr is SnipeApiReadOnlyUserAttribute<T> typedAttr)
				{
					attr = typedAttr;
					return true;
				}
			}

			attr = null;
			return false;
		}

		~SnipeApiUserAttributes()
		{
			Dispose();
		}

		public void Dispose()
		{
			lock (_attributesLock)
			{
				foreach (var attr in _attributes)
				{
					if (attr.Value is IDisposable disposable)
					{
						try
						{
							disposable.Dispose();
						}
						catch (Exception)
						{
						}
					}
				}
			}
		}

		private void OnMessageReceived(string message_type, string error_code, IDictionary<string, object> data, int request_id)
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
					foreach (IDictionary<string, object> o in list)
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
