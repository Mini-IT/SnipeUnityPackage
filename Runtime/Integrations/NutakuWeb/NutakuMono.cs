#if NUTAKU_WEB
using System.Runtime.InteropServices;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public sealed class NutakuMono : MonoBehaviour
	{
		private sealed class HandshakeResponse
		{
			public string errorCode;
			public string token;
		}

		public sealed class UserInfoResponse
		{
			public int id { get; set; }
			public string nickname { get; set; }

			public int test { get; set; }
			public int grade { get; set; }

			public string titleId { get; set; }
			public string language { get; set; }
			public string gameType { get; set; }
		}


		[DllImport("__Internal")]
		private static extern string GetUserInfo_js();

		[DllImport("__Internal")]
		private static extern string GetHandshake_js();

		public static NutakuMono Instance
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

		public UserInfoResponse UserInfo { get; private set; }

		private static NutakuMono _instance;

		public void OnHandshakeReceived(string json)
		{
			UnityEngine.Debug.Log("[NutakuMono] OnHandshakeReceived");
			var handshake = fastJSON.JSON.ToObject<HandshakeResponse>(json);
			if (handshake?.errorCode == "ok")
			{
				HandshakeToken = handshake.token;
				SetIsInitialized();
			}
		}

		public void OnUserInfoReceived(string json)
		{
			UnityEngine.Debug.Log("[NutakuMono] OnUserInfoReceived");
			UserInfo = fastJSON.JSON.ToObject<UserInfoResponse>(json);
			UserId = UserInfo?.id.ToString();

			SetIsInitialized();
		}

		private static void CreateMono()
		{
			Instance = new GameObject("NutakuMono").AddComponent<NutakuMono>();
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
			GetUserInfo_js();
			GetHandshake_js();
		}
	}
}
#endif
