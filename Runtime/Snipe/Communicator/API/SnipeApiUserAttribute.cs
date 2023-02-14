using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Api
{
	public class SnipeApiUserAttribute
	{
		private const int SET_REQUEST_DELAY_MILLISECONDS = 300;

		public delegate void ValueChangedHandler(object oldValue, object value);
		public delegate void SetCallback(string errorCode, string key, object value);

		public class SetRequest
		{
			internal object _value;
			internal SetCallback _callback;
		}

		protected static readonly Dictionary<string, SetRequest> _setRequests = new Dictionary<string, SetRequest>();
		protected static readonly SemaphoreSlim _setRequestsSemaphore = new SemaphoreSlim(1, 1);
		protected static CancellationTokenSource _setRequestsCancellation;

		protected readonly AbstractSnipeApiService _snipeApi;
		protected readonly string _key;

		public SnipeApiUserAttribute(AbstractSnipeApiService snipeApi, string key)
		{
			_snipeApi = snipeApi;
			_key = key;
		}

		protected async void AddSetRequest(object val, SetCallback callback = null)
		{
			try
			{
				await _setRequestsSemaphore.WaitAsync();

				SetRequest request;
				if (!_setRequests.TryGetValue(_key, out request))
				{
					request = new SetRequest();
					_setRequests.Add(_key, request);
				}
				request._value = val;
				if (callback != null)
					request._callback += callback;

				if (_setRequestsCancellation == null)
				{
					_setRequestsCancellation = new CancellationTokenSource();
					DelayedSendSetRequests(_setRequestsCancellation.Token);
				}
			}
			finally
			{
				_setRequestsSemaphore.Release();
			}
		}

		private async void DelayedSendSetRequests(CancellationToken cancellationToken)
		{
			try
			{
				await Task.Delay(SET_REQUEST_DELAY_MILLISECONDS, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			List<SnipeObject> attrs = null;
			List<SetCallback> callbacks = null;

			try
			{
				await _setRequestsSemaphore.WaitAsync();

				if (_setRequests.Count > 0)
				{
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
				}

				_setRequestsCancellation = null;
			}
			finally
			{
				_setRequestsSemaphore.Release();
			}

			if (attrs == null)
				return;

			var request = _snipeApi.CreateRequest("attr.setMulti", new SnipeObject()
			{
				["data"] = attrs,
			});

			if (request == null)
				return;

			request.Request((error_code, response_data) =>
			{
				if (callbacks == null)
					return;

				foreach(var callback in callbacks)
				{
					callback?.Invoke(error_code,
						response_data.SafeGetString("key"),
						response_data["val"]);
				}
			});
		}
	}

	public class SnipeApiUserAttribute<ValueType> : SnipeApiUserAttribute
	{
		private ValueType _value;

		public SnipeApiUserAttribute(AbstractSnipeApiService snipeApi, string key) : base(snipeApi, key)
		{
		}

		public ValueType GetValue()
		{
			return _value;
		}

		public void SetValue(ValueType val, SetCallback callback = null)
		{
			if (object.Equals(_value, val))
			{
				callback?.Invoke("ok", _key, val);
				return;
			}
			_value = val;

			AddSetRequest(val, callback);
		}

		public static implicit operator ValueType(SnipeApiUserAttribute<ValueType> attr)
		{
			return attr._value;
		}
	}
}
