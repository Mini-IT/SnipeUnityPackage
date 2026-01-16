#if UNITY_WEBGL
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuSnipeMono : MonoBehaviour
	{
#if UNITY_WEBGL
		[DllImport("__Internal")]
		private static extern string GetUserId_js();

		[DllImport("__Internal")]
		private static extern string GetHandshake_js();
#endif
		internal sealed class HandshakeResponse
		{
			public string errorCode;
			public string token;
		}

		public bool IsInitialized { get; private set; }
		public string HandshakeToken { get; private set; }
		public string UserId { get; private set; }

		//JS callback
		public void OnHandshakeReceived(string json)
		{
			var handshake = fastJSON.JSON.ToObject<HandshakeResponse>(json);
			if (handshake?.errorCode == "ok")
			{
				HandshakeToken = handshake.token;
				SetIsInitialized();
			}
		}

		//JS callback
		public void OnUserIdReceived(string userId)
		{
			UserId = userId;
			SetIsInitialized();
		}

		private void SetIsInitialized()
		{
			if (string.IsNullOrEmpty(HandshakeToken) || string.IsNullOrEmpty(UserId))
			{
				return;
			}

			IsInitialized = true;
		}

		private void Awake()
		{
			DontDestroyOnLoad(gameObject);
#if UNITY_WEBGL
			GetUserId_js();
			GetHandshake_js();
#endif
		}
	}
}
