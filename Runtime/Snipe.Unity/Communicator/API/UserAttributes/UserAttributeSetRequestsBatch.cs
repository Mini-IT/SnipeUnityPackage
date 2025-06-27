using System.Collections.Generic;

using SetCallback = MiniIT.Snipe.Api.SnipeApiUserAttribute.SetCallback;

namespace MiniIT.Snipe.Api
{
	public class UserAttributeSetRequestsBatch
	{
		private class SetRequest
		{
			internal object _value;
			internal SetCallback _callback;
		}

		private readonly Dictionary<string, SetRequest> _setRequests = new Dictionary<string, SetRequest>();

		public void AddSetRequest(string key, object val, SetCallback callback = null)
		{
			if (!_setRequests.TryGetValue(key, out SetRequest request))
			{
				request = new SetRequest();
				_setRequests.Add(key, request);
			}
			request._value = val;
			if (callback != null)
			{
				request._callback += callback;
			}
		}

		public bool TryFlushSingle(out string key, out object val, out SetCallback callback)
		{
			if (_setRequests.Count == 1)
			{
				foreach (var item in _setRequests)
				{
					key = item.Key;
					val = item.Value._value;
					callback = item.Value._callback;
					_setRequests.Clear();
					return true;
				}
			}

			key = null;
			val = null;
			callback = null;
			return false;
		}

		public bool TryFlush(out List<IDictionary<string, object>> attrs, out List<SetCallback> callbacks)
		{
			if (_setRequests.Count <= 0)
			{
				attrs = null;
				callbacks = null;
				return false;
			}

			attrs = new List<IDictionary<string, object>>(_setRequests.Count);
			callbacks = new List<SetCallback>(_setRequests.Count);

			foreach (var req in _setRequests)
			{
				attrs.Add(new Dictionary<string, object>()
				{
					["key"] = req.Key,
					["val"] = req.Value._value,
					["action"] = "set",
				});
				callbacks.Add(req.Value._callback);
			}

			_setRequests.Clear();

			return true;
		}
	}
}
