using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MiniIT.Threading;
using MiniIT.Threading.Tasks;

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
			{
				return true;
			}

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
			{
				return false;
			}

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

	public class SnipeApiUserAttribute<TAttrValue> : SnipeApiReadOnlyUserAttribute<TAttrValue>, IDisposable
	{
		// TODO: Move to more externaly available place
		public static TimeSpan RequestDelay = TimeSpan.FromMilliseconds(900);

		protected static readonly AlterSemaphore _setRequestsSemaphore = new AlterSemaphore(1, 1);
		protected static UserAttributeSetRequestsBatch _requests;
		protected static CancellationTokenSource _setRequestsCancellation;

		public SnipeApiUserAttribute(AbstractSnipeApiService snipeApi, string key)
			: base(snipeApi, key) { }

		protected override void DoSetValue(TAttrValue val, SetCallback callback = null)
		{
			if (AreEqual(val, _value))
			{
				callback?.Invoke("ok", _key, _value);
			}
			else
			{
				TAttrValue oldValue = _value;
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

				_requests ??= new UserAttributeSetRequestsBatch();
				_requests.AddSetRequest(_key, val, callback);

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
				await AlterTask.Delay(RequestDelay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			try
			{
				await _setRequestsSemaphore.WaitAsync(cancellationToken);

				_setRequestsCancellation?.Dispose();
				_setRequestsCancellation = null;

				FlushRequests();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			finally
			{
				_setRequestsSemaphore.Release();
			}
		}

		private void FlushRequests()
		{
			if (!_requests.TryFlush(out List<SnipeObject> attrs, out List<SetCallback> callbacks))
			{
				return;
			}

			var request = _snipeApi.CreateRequest("attr.setMulti", new SnipeObject()
			{
				["data"] = attrs,
			});

			request?.Request((error_code, response_data) =>
			{
				if (callbacks == null)
				{
					return;
				}

				foreach (var callback in callbacks)
				{
					callback?.Invoke(error_code,
						response_data.SafeGetString("key"),
						response_data["val"]);
				}
			});
		}

		public void Dispose()
		{
			if (_setRequestsCancellation != null)
			{
				_setRequestsCancellation.Cancel();
				_setRequestsCancellation.Dispose();
				_setRequestsCancellation= null;
			}

			_requests?.TryFlush(out _, out _);
		}

		public static implicit operator TAttrValue(SnipeApiUserAttribute<TAttrValue> attr)
		{
			return attr._value;
		}
	}
}
