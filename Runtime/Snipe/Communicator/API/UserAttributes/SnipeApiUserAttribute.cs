using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace MiniIT.Snipe.Api
{
	public abstract class SnipeApiUserAttribute
	{
		public delegate void SetCallback(string errorCode, string key, object value);
		
		public string Key => _key;

		protected readonly AbstractSnipeApiService _snipeApi;
		protected readonly string _key;

		internal SnipeApiUserAttribute(AbstractSnipeApiService snipeApi, string key)
		{
			_snipeApi = snipeApi;
			_key = key;
		}

		public abstract void SetValue(object val, SetCallback callback = null);
	}

	public class SnipeApiReadOnlyUserAttribute<AttrValueType> : SnipeApiUserAttribute
	{
		
		public delegate void ValueChangedHandler(AttrValueType oldValue, AttrValueType value);
		public event ValueChangedHandler ValueChanged;

		protected AttrValueType _value;
		protected bool _initialized;

		public SnipeApiReadOnlyUserAttribute(AbstractSnipeApiService snipeApi, string key) : base(snipeApi, key)
		{
			_initialized = false;
		}

		public AttrValueType GetValue()
		{
			return _value;
		}

		public override void SetValue(object val, SetCallback callback = null)
		{
			AttrValueType value = (AttrValueType)Convert.ChangeType(val, typeof(AttrValueType));
			DoSetValue(value, callback);
		}

		public void SetValue(AttrValueType val, SetCallback callback = null)
		{
			DoSetValue(val, callback);
		}

		protected virtual void DoSetValue(AttrValueType val, SetCallback callback = null)
		{
			var oldValue = _value;
			_value = val;

			callback?.Invoke("ok", _key, _value);

			if (_initialized && !Equals(oldValue, _value))
			{
				RaiseValueChangedEvent(oldValue, _value);
			}

			_initialized = true;
		}

		protected void RaiseValueChangedEvent(AttrValueType oldValue, AttrValueType newValue)
		{
			ValueChanged?.Invoke(oldValue, newValue);
		}

		public static implicit operator AttrValueType(SnipeApiReadOnlyUserAttribute<AttrValueType> attr)
		{
			return attr._value;
		}
	}

	public class SnipeApiUserAttribute<AttrValueType> : SnipeApiReadOnlyUserAttribute<AttrValueType>
	{
		private const int SET_REQUEST_DELAY_MILLISECONDS = 300;

		public class SetRequest
		{
			internal object _value;
			internal SetCallback _callback;
		}

		protected static readonly Dictionary<string, SetRequest> _setRequests = new Dictionary<string, SetRequest>();
		protected static readonly SemaphoreSlim _setRequestsSemaphore = new SemaphoreSlim(1, 1);
		protected static CancellationTokenSource _setRequestsCancellation;

		public SnipeApiUserAttribute(AbstractSnipeApiService snipeApi, string key) : base(snipeApi, key)
		{
		}

		protected override void DoSetValue(AttrValueType val, SetCallback callback = null)
		{
			if (Equals(val, _value))
			{
				callback?.Invoke("ok", _key, _value);
				_initialized = true;
				return;
			}

			var oldValue = _value;
			_value = val;

			if (_initialized)
			{
				RaiseValueChangedEvent(oldValue, _value);
				AddSetRequest(_value, callback);
			}

			_initialized = true;
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

				foreach (var callback in callbacks)
				{
					callback?.Invoke(error_code,
						response_data.SafeGetString("key"),
						response_data["val"]);
				}
			});
		}

		public static implicit operator AttrValueType(SnipeApiUserAttribute<AttrValueType> attr)
		{
			return attr._value;
		}
	}
}
