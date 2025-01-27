
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MiniIT.Http
{
	public class UnityHttpClient : IHttpClient
	{
		private string _authToken;

		public void Reset()
		{
			_authToken = null;
		}

		public void SetAuthToken(string token)
		{
			_authToken = token;
		}

		public async UniTask<IHttpClientResponse> GetAsync(Uri uri)
		{
			var request = UnityWebRequest.Get(uri);
			return await SendRequestAsync(request);
		}

		public async UniTask<IHttpClientResponse> PostJsonAsync(Uri uri, string content)
		{
			var request = UnityWebRequest.Post(uri, content, "application/json");
			if (!string.IsNullOrEmpty(_authToken))
			{
				request.SetRequestHeader("Authorization", "Bearer " + _authToken);
			}

			return await SendRequestAsync(request);
		}

		public async UniTask<IHttpClientResponse> PostAsync(Uri uri, string name, byte[] content)
		{
			var form = new WWWForm();
			form.AddBinaryData(name, content);

			var request = UnityWebRequest.Post(uri, form);
			if (!string.IsNullOrEmpty(_authToken))
			{
				request.SetRequestHeader("Authorization", "Bearer " + _authToken);
			}

			return await SendRequestAsync(request);
		}

		private static async UniTask<IHttpClientResponse> SendRequestAsync(UnityWebRequest request)
		{
			request.downloadHandler = new DownloadHandlerBuffer();
			await request.SendWebRequest().ToUniTask();
			return new UnityHttpClientResponse(request);
		}
	}
}
