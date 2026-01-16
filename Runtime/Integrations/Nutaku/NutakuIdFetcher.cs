using System;
using Cysharp.Threading.Tasks;
using MiniIT.Threading;
using UnityEngine;
#if NUTAKU && UNITY_ANDROID
using NutakuUnitySdk;
#endif

namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuIdFetcher : AuthIdFetcher, IAuthIdFetcherWithToken
	{
		private const int INITIALIZATION_DELAY = 100;
		public string Token { get; private set; }

		private Action<string> _callback;
		private readonly object _monoLock = new object();
		private NutakuSnipeMono _mono;

		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			_callback = callback;
			CreateMono();
#if UNITY_WEBGL
			if (wait_initialization && string.IsNullOrEmpty(Value))
			{
				WaitForInitialization().Forget();
				return;
			}
			SetValue(_mono.UserId);
			SetToken(_mono.HandshakeToken);
			_callback?.Invoke(Value);
#elif UNITY_ANDROID
			if (string.IsNullOrEmpty(Value))
			{
				string userId = NutakuCurrentUser.GetUserId().ToString();
				SetValue(userId);
			}
			GetHandshake();
#endif
		}

		private void CreateMono()
		{
			lock (_monoLock)
			{
				var nutakuObj = new GameObject("NutakuSnipeMono");
				_mono = nutakuObj.AddComponent<NutakuSnipeMono>();
			}
		}

#if UNITY_ANDROID
		private void GetHandshake()
		{
			lock (_monoLock)
			{
				NutakuApi.GameHandshake(_mono, OnHandshakeReceived);
			}
		}

		private void OnHandshakeReceived(NutakuApiRawResult rawresult)
		{
			if (rawresult.responseCode is > 199 and < 300)
			{
				var result = NutakuApi.Parse_GameHandshake(rawresult);
				var handshake = fastJSON.JSON.ToObject<NutakuSnipeMono.HandshakeResponse>(result.message);
				if (handshake?.errorCode == "ok")
				{
					SetToken(_mono.HandshakeToken);
					_callback?.Invoke(Value);
				}

			}
			lock (_monoLock)
			{
				var monoGameObject = _mono.gameObject;
				_mono = null;
				GameObject.Destroy(monoGameObject);
			}
		}
#endif

		private async UniTaskVoid WaitForInitialization()
		{
			while (!_mono.IsInitialized)
			{
				await AlterTask.Delay(INITIALIZATION_DELAY);
			}

			SetValue(_mono.UserId);
			SetToken(_mono.HandshakeToken);

			_callback?.Invoke(Value);
		}

		private void SetToken(string value)
		{
			Token = CheckValueValid(value) ? value : "";
		}

		private void SetValue(string value)
		{
			Value = CheckValueValid(value) ? value : "";
		}

		private static bool CheckValueValid(string value)
		{
			return !string.IsNullOrEmpty(value);
		}
	}
}
