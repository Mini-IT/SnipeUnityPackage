#if NUTAKU

using System;
using Newtonsoft.Json;
using NutakuUnitySdk;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public class NutakuIdFetcher : AuthIdFetcher
	{
		public class NutakuMono : MonoBehaviour
		{
			private void Awake()
			{
				DontDestroyOnLoad(gameObject);
			}
		}

		private class Handshake
		{
			public string errorCode;
			public string token;
		}

		private Action<string> _callback;
		private NutakuMono _mono;

		public override void Fetch(bool wait_initialization, Action<string> callback = null)
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
			var nutakuObj = GameObject.Instantiate(new GameObject());
			nutakuObj.name = "NutakuHandshakeMono";
			_mono = nutakuObj.AddComponent<NutakuMono>();
		}

		private void GetHandshake()
		{
			NutakuApi.GameHandshake(_mono, OnHandshakeReceived);
		}

		private void OnHandshakeReceived(NutakuApiRawResult rawresult)
		{
			if(rawresult.responseCode is > 199 and < 300)
			{
				var result = NutakuApi.Parse_GameHandshake(rawresult);
				var handshake = JsonConvert.DeserializeObject<Handshake>(result.message);
				if (handshake is { errorCode: "ok" })
				{
					Token = handshake.token;
					_callback?.Invoke(Value);
					GameObject.Destroy(_mono);
				}
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
