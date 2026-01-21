using System;
using Cysharp.Threading.Tasks;
using MiniIT.Threading;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public abstract class AbstractNutakuIdFetcher : AuthIdFetcher, IAuthIdFetcherWithToken
	{
		private const int INITIALIZATION_DELAY = 100;
		public string Token { get; private set; }

		private Action<string> _callback;
		protected readonly object _monoLock = new object();
		protected NutakuSnipeMono _mono;

		public override void Fetch(bool waitInitialization, Action<string> callback = null)
		{
			_callback = callback;
			CreateMono();
			InternalFetch(waitInitialization, callback);
		}

		protected abstract void InternalFetch(bool waitInitialization, Action<string> callback = null);

		private void CreateMono()
		{
			lock (_monoLock)
			{
				if (_mono)
				{
					return;
				}
				var nutakuObj = new GameObject(nameof(NutakuSnipeMono));
				_mono = nutakuObj.AddComponent<NutakuSnipeMono>();
			}
		}

		protected void DestroyMono()
		{
			lock (_monoLock)
			{
				if (!_mono)
				{
					return;
				}
				var monoGameObject = _mono.gameObject;
				_mono = null;
				GameObject.Destroy(monoGameObject);
			}
		}

		protected async UniTaskVoid WaitForInitialization()
		{
			bool initialized;
			lock (_monoLock)
			{
				initialized = _mono && _mono.IsInitialized;
			}

			while (!initialized)
			{
				await AlterTask.Delay(INITIALIZATION_DELAY);

				lock (_monoLock)
				{
					initialized = _mono && _mono.IsInitialized;
				}
			}

			SetValuesFromMono();
		}

		protected void SetValuesFromMono()
		{
			lock (_monoLock)
			{
				SetValue(_mono.UserId);
				SetToken(_mono.HandshakeToken);
			}

			InvokeCallback(Value);
		}

		protected void SetToken(string value)
		{
			Token = CheckValueValid(value) ? value : "";
		}

		protected void SetValue(string value)
		{
			Value = CheckValueValid(value) ? value : "";
		}

		private static bool CheckValueValid(string value)
		{
			return !string.IsNullOrEmpty(value);
		}

		protected void InvokeCallback(string value)
		{
			_callback?.Invoke(value);
		}
	}
}
