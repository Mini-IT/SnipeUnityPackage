#if NUTAKU && UNITY_ANDROID

using System;
using NutakuUnitySdk;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public sealed class AndroidNutakuIdFetcher : AbstractNutakuIdFetcher
	{
		protected override void InternalFetch(bool waitInitialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				string userId = NutakuCurrentUser.GetUserId().ToString();
				SetValue(userId);
			}
			GetHandshake();
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
				var handshake = fastJSON.JSON.ToObject<NutakuSnipeMono.HandshakeResponse>(result.message);
				if (handshake?.errorCode == "ok")
				{
					SetToken(handshake.token);
					InvokeCallback(Value);
				}

			}

			DestroyMono();
		}
	}
}

#endif
