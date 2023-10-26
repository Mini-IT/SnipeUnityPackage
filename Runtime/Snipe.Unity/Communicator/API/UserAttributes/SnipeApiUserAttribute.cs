using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

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

		public static bool AreEqual<T>(T objA, T objB)
		{
			if (ReferenceEquals(objA, objB))
				return true;

			if (objA == null)
			{
				if (objB == null)
					return true;
				if (objB is ICollection colB)
					return (colB.Count == 0);

				return false;
			}

			if (objB == null)
			{
				if (objA is ICollection colA)
					return (colA.Count == 0);
				return false;
			}

			if (objA.GetType() != objB.GetType())
				return false;

			if (objA is ICollection collectionA && objB is ICollection collectionB)
			{
				return AreCollectionsEqual(collectionA, collectionB);
			}

			return objA.Equals(objB);
		}

		private static bool AreCollectionsEqual(ICollection collectionA, ICollection collectionB)
		{
			if (collectionA.Count != collectionB.Count)
				return false;

			IEnumerator enumeratorA = collectionA.GetEnumerator();
			IEnumerator enumeratorB = collectionB.GetEnumerator();

			while (enumeratorA.MoveNext() && enumeratorB.MoveNext())
			{
				if (!AreEqual(enumeratorA.Current, enumeratorB.Current))
					return false;
			}

			return true;
		}
	}

	public class SnipeApiReadOnlyUserAttribute<TAttrValue> : SnipeApiUserAttribute
	{
		public delegate void ValueChangedHandler(TAttrValue oldValue, TAttrValue value);
		public event ValueChangedHandler ValueChanged;

		protected TAttrValue _value;
		protected bool _initialized;

		public SnipeApiReadOnlyUserAttribute(AbstractSnipeApiService snipeApi, string key) : base(snipeApi, key)
		{
			_initialized = false;
		}

		public TAttrValue GetValue()
		{
			return _value;
		}

		public override void SetValue(object val, SetCallback callback = null)
		{
			TAttrValue value = TypeConverter.Convert<TAttrValue>(val);
			DoSetValue(value, callback);
		}

		public void SetValue(TAttrValue val, SetCallback callback = null)
		{
			DoSetValue(val, callback);
		}

		protected virtual void DoSetValue(TAttrValue val, SetCallback callback = null)
		{
			var oldValue = _value;
			_value = val;

			callback?.Invoke("ok", _key, _value);

			if (_initialized && !AreEqual(oldValue, _value))
			{
				RaiseValueChangedEvent(oldValue, _value);
			}

			_initialized = true;
		}

		protected void RaiseValueChangedEvent(TAttrValue oldValue, TAttrValue newValue)
		{
			ValueChanged?.Invoke(oldValue, newValue);
		}

		public override string ToString() => Convert.ToString(_value);

		public static implicit operator TAttrValue(SnipeApiReadOnlyUserAttribute<TAttrValue> attr)
		{
			return attr._value;
		}
	}

	public class SnipeApiUserAttribute<TAttrValue> : SnipeApiReadOnlyUserAttribute<TAttrValue>
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

		protected override void DoSetValue(TAttrValue val, SetCallback callback = null)
		{
			if (AreEqual(val, _value))
			{
				callback?.Invoke("ok", _key, _value);
			}
			else
			{
				var oldValue = _value;
				_value = val;

				if (_initialized)
				{
					RaiseValueChangedEvent(oldValue, _value);
					AddSetRequest(_value, callback);
				}
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

		public static implicit operator TAttrValue(SnipeApiUserAttribute<TAttrValue> attr)
		{
			return attr._value;
		}
	}
}
