#if NUTAKU

using System;
using NutakuUnitySdk;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuIdFetcher : AuthIdFetcher, IAuthIdFetcherWithToken
	{
		public string Token { get; private set; }

		public sealed class NutakuMono : MonoBehaviour
		{
			private void Awake()
			{
				DontDestroyOnLoad(gameObject);
			}
		}

		private sealed class HandshakeResponse
		{
			public string errorCode;
			public string token;
		}

		private Action<string> _callback;
		private NutakuMono _mono;
		private readonly object _monoLock = new object();

		public override void Fetch(bool waitInitialization, Action<string> callback = null)
		{
			_callback = callback;

			if (string.IsNullOrEmpty(Value))
			{
				string userId = NutakuCurrentUser.GetUserId().ToString();
				SetValue(userId);
			}

			CreateMono();
			GetHandshake();
		}

		private void CreateMono()
		{
			lock (_monoLock)
			{
				var nutakuObj = new GameObject("NutakuHandshakeMono");
				_mono = nutakuObj.AddComponent<NutakuMono>();
			}
		}

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
				var handshake = fastJSON.JSON.ToObject<HandshakeResponse>(result.message);
				if (handshake?.errorCode == "ok")
				{
					Token = handshake.token;
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

#endif
