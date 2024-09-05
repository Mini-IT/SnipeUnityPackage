using System.Collections.Generic;

using SetCallback = MiniIT.Snipe.Api.SnipeApiUserAttribute.SetCallback;

namespace MiniIT.Snipe.Api
{
	public class UserAttributeSetRequestsBatch
	{
		public class SetRequest
		{
			internal object _value;
			internal SetCallback _callback;
		}
		
		protected readonly Dictionary<string, SetRequest> _setRequests = new Dictionary<string, SetRequest>();

		public void AddSetRequest(string key, object val, SetCallback callback = null)
		{
			SetRequest request;
			if (!_setRequests.TryGetValue(key, out request))
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

		public bool TryFlush(out List<SnipeObject> attrs, out List<SetCallback> callbacks)
		{
			if (_setRequests.Count <= 0)
			{
				attrs = null;
				callbacks = null;
				return false;
			}

			attrs = new List<SnipeObject>(_setRequests.Count);
			callbacks = new List<SetCallback>(_setRequests.Count);

			foreach (var req in _setRequests)
			{
				attrs.Add(new SnipeObject()
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
