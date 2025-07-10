
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MiniIT.Http
{
	public class UnityHttpClient : IHttpClient
	{
		private string _authToken;
		private string _persistentClientId;

		public void Reset()
		{
			_authToken = null;
		}

		public void SetAuthToken(string token)
		{
			_authToken = token;
		}

		public void SetPersistentClientId(string id)
		{
			_persistentClientId = id;
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri)
		{
			var request = UnityWebRequest.Get(uri);
			return await SendRequestAsync(request);
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout)
		{
			var request = UnityWebRequest.Get(uri);
			request.timeout = (int)timeout.TotalSeconds;
			return await SendRequestAsync(request);
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string content)
		{
			var request = UnityWebRequest.Post(uri, content, "application/json");
			FillHeaders(request);

			return await SendRequestAsync(request);
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content)
		{
			var form = new WWWForm();
			form.AddBinaryData(name, content);

			var request = UnityWebRequest.Post(uri, form);
			FillHeaders(request);

			return await SendRequestAsync(request);
		}

		private void FillHeaders(UnityWebRequest request)
		{
			if (!string.IsNullOrEmpty(_authToken))
			{
				request.SetRequestHeader("Authorization", "Bearer " + _authToken);
			}

			if (!string.IsNullOrEmpty(_persistentClientId))
			{
				request.SetRequestHeader("DeviceID", _persistentClientId);
			}
		}

		private async UniTask<IHttpClientResponse> SendRequestAsync(UnityWebRequest request)
		{
			request.downloadHandler = new DownloadHandlerBuffer();
			await request.SendWebRequest().ToUniTask();
			return new UnityHttpClientResponse(request);
		}
	}
}
