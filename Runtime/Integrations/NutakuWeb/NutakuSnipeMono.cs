#if NUTAKU_WEB
using System.Runtime.InteropServices;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuSnipeMono : MonoBehaviour
	{
		[DllImport("__Internal")]
		private static extern string GetUserId_js();

		[DllImport("__Internal")]
		private static extern string GetHandshake_js();

		private sealed class HandshakeResponse
		{
			public string errorCode;
			public string token;
		}

		public static NutakuSnipeMono Instance
		{
			get
			{
				if (_instance == null)
				{
					CreateMono();
				}

				return _instance;
			}
			private set
			{
				_instance = value;
			}
		}

		public bool IsInitialized { get; private set; }

		public string HandshakeToken { get; private set; }
		public string UserId { get; private set; }
		

		private static NutakuSnipeMono _instance;

		public void OnHandshakeReceived(string json)
		{
			UnityEngine.Debug.Log("[NutakuSnipeMono] OnHandshakeReceived");
			var handshake = fastJSON.JSON.ToObject<HandshakeResponse>(json);
			if (handshake?.errorCode == "ok")
			{
				HandshakeToken = handshake.token;
				SetIsInitialized();
			}
		}

		public void OnUserIdReceived(string userId)
		{
			UnityEngine.Debug.Log("[NutakuSnipeMono] OnUserInfoReceived");
			UserId = userId;
			SetIsInitialized();
		}

		private static void CreateMono()
		{
			Instance = new GameObject("NutakuSnipeMono").AddComponent<NutakuSnipeMono>();
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
			GetUserId_js();
			GetHandshake_js();
		}
	}
}
#endif
