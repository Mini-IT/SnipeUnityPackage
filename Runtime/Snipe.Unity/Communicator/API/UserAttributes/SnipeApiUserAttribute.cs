using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MiniIT.Threading;
using MiniIT.Utils;

namespace MiniIT.Snipe.Api
{
	public abstract class SnipeApiUserAttribute
	{
		public static TimeSpan RequestDelay { get; set; } = TimeSpan.FromMilliseconds(900);

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
		internal abstract void SetValueWithoutEvent(object val, SetCallback callback = null);
		internal abstract void RaisePendingValueChangedEvent();

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
			{
				return false;
			}

			bool areEqual = true;

			IEnumerator enumeratorA = collectionA.GetEnumerator();
			IEnumerator enumeratorB = collectionB.GetEnumerator();

			while (enumeratorA.MoveNext() && enumeratorB.MoveNext())
			{
				if (!AreEqual(enumeratorA.Current, enumeratorB.Current))
				{
					areEqual = false;
					break;
				}
			}

			if (enumeratorA is IDisposable disposableA)
			{
				disposableA.Dispose();
			}

			if (enumeratorB is IDisposable disposableB)
			{
				disposableB.Dispose();
			}

			return areEqual;
		}
	}

	public class SnipeApiReadOnlyUserAttribute<TAttrValue> : SnipeApiUserAttribute
	{
		protected class PendingChange
		{
			internal TAttrValue _oldValue;
			internal TAttrValue _newValue;
		}

		public delegate void ValueChangedHandler(TAttrValue oldValue, TAttrValue value);
		public event ValueChangedHandler ValueChanged;

		public bool IsInitialized => _initialized;

		protected TAttrValue _value;
		protected bool _initialized;

		protected PendingChange _pendingChange;
		protected SetCallback _pendingSetCallback;

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
			SetValue(value, callback);
		}

		public void SetValue(TAttrValue val, SetCallback callback = null)
		{
			var prevPendingChange = _pendingChange;

			SetValueWithoutEvent(val, callback);
			RaisePendingValueChangedEvent();

			if (prevPendingChange != _pendingChange)
			{
				AddSetRequest(val, callback);
			}
		}

		internal override void SetValueWithoutEvent(object val, SetCallback callback = null)
		{
			TAttrValue value = TypeConverter.Convert<TAttrValue>(val);
			SetValueWithoutEvent(value, callback);
		}

		protected virtual void SetValueWithoutEvent(TAttrValue val, SetCallback callback = null)
		{
			var oldValue = _value;
			_value = val;

			_pendingSetCallback = callback;

			if (_initialized && !AreEqual(oldValue, _value))
			{
				_pendingChange = new PendingChange()
				{
					_oldValue = oldValue,
					_newValue = val,
				};
			}

			_initialized = true;
		}

		internal override void RaisePendingValueChangedEvent()
		{
			_pendingSetCallback?.Invoke("ok", _key, _value);
			_pendingSetCallback = null;

			if (_pendingChange == null)
			{
				return;
			}

			RaiseValueChangedEvent(_pendingChange._oldValue, _pendingChange._newValue);
			_pendingChange = null;
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

		protected virtual void AddSetRequest(object val, SetCallback callback = null)
		{
			// nothing
		}
	}

	public class SnipeApiUserAttribute<TAttrValue> : SnipeApiReadOnlyUserAttribute<TAttrValue>, IDisposable
	{
		private readonly SnipeApiUserAttributeSyncronizer _syncronizer = SnipeApiUserAttributeSyncronizer.Instance;

		public SnipeApiUserAttribute(AbstractSnipeApiService snipeApi, string key)
			: base(snipeApi, key) { }

		protected override void SetValueWithoutEvent(TAttrValue val, SetCallback callback = null)
		{
			if (AreEqual(val, _value))
			{
				_pendingSetCallback = callback;
			}
			else
			{
				TAttrValue oldValue = _value;
				_value = val;

				_pendingSetCallback = null;

				if (_initialized)
				{
					_pendingChange = new PendingChange()
					{
						_oldValue = oldValue,
						_newValue = val,
					};
				}
			}

			_initialized = true;
		}

		protected override async void AddSetRequest(object val, SetCallback callback = null)
		{
			bool semaphoreOccupied = false;

			try
			{
				await _syncronizer.Semaphore.WaitAsync();
				semaphoreOccupied = true;

				var requests = _syncronizer.GetRequests(true);
				requests.AddSetRequest(_key, val, callback);

				if (_syncronizer.Cancellation == null)
				{
					_syncronizer.InitCancellation();
					DelayedSendSetRequests(_syncronizer.Cancellation.Token);
				}
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_syncronizer.Semaphore.Release();
				}
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

			bool semaphoreOccupied = false;

			try
			{
				await _syncronizer.Semaphore.WaitAsync(cancellationToken);
				semaphoreOccupied = true;

				_syncronizer.DisposeCancellation();

				FlushRequests();
			}
			catch (OperationCanceledException)
			{
				// Ignore
			}
			finally
			{
				if (semaphoreOccupied)
				{
					_syncronizer.Semaphore.Release();
				}
			}
		}

		private void FlushRequests()
		{
			var requests = _syncronizer.GetRequests(false);

			if (requests == null)
			{
				return;
			}

			if (requests.TryFlushSingle(out string key, out object val, out SetCallback callback))
			{
				var req = _snipeApi.CreateRequest("attr.set", new Dictionary<string, object>() { ["key"] = key, ["val"] = val });
				req?.Request((errorCode, responseData) => callback?.Invoke(errorCode,
					responseData.SafeGetString("key"),
					responseData.SafeGetValue<object>("val")));
				return;
			}

			if (!requests.TryFlush(out List<IDictionary<string, object>> attrs, out List<SetCallback> callbacks))
			{
				return;
			}

			var request = _snipeApi.CreateRequest("attr.setMulti", new Dictionary<string, object>()
			{
				["data"] = attrs,
			});

			request?.Request((errorCode, responseData) =>
			{
				if (callbacks == null)
				{
					return;
				}

				foreach (var callback in callbacks)
				{
					callback?.Invoke(errorCode,
						responseData.SafeGetString("key"),
						responseData["val"]);
				}
			});
		}

		public void Dispose()
		{
			_syncronizer.Clear();
		}

		public static implicit operator TAttrValue(SnipeApiUserAttribute<TAttrValue> attr)
		{
			return attr._value;
		}
	}

	internal class SnipeApiUserAttributeSyncronizer
	{
		public static SnipeApiUserAttributeSyncronizer Instance { get; } = new SnipeApiUserAttributeSyncronizer();

		private SnipeApiUserAttributeSyncronizer()
		{
		}

		public AlterSemaphore Semaphore { get; } = new AlterSemaphore(1, 1);

		public CancellationTokenSource Cancellation => _cancellation;

		private UserAttributeSetRequestsBatch _requests;
		private CancellationTokenSource _cancellation;

		public UserAttributeSetRequestsBatch GetRequests(bool create)
		{
			if (_requests == null && create)
			{
				_requests = new UserAttributeSetRequestsBatch();
			}
			return _requests;
		}

		public void Clear()
		{
			DisposeCancellation();

			_requests?.TryFlush(out _, out _);
		}

		public void InitCancellation()
		{
			_cancellation ??= new CancellationTokenSource();
		}

		public void DisposeCancellation()
		{
			CancellationTokenHelper.CancelAndDispose(ref _cancellation);
		}
	}
}
