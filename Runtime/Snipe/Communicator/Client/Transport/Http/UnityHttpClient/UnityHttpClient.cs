
using System;
using Cysharp.Threading.Tasks;
using MiniIT.Threading.Tasks;
using UnityEngine.Networking;

namespace MiniIT.Snipe.Internal
{
	public class UnityHttpClient : IHttpTransportClient
	{
		string _authToken;

		public void Reset()
		{
			_authToken = null;
		}

		public void SetAuthToken(string token)
		{
			_authToken = token;
		}

		public async AlterTask<IHttpTransportClientResponse> GetAsync(Uri uri)
		{
			var request = UnityWebRequest.Get(uri.ToString());
			return await SendRequestAsync(request);
		}

		public async AlterTask<IHttpTransportClientResponse> PostJsonAsync(Uri uri, string content)
		{
			var request = UnityWebRequest.Post(uri.ToString(), content, "application/json");
			if (!string.IsNullOrEmpty(_authToken))
			{
				request.SetRequestHeader("Authorization", "Bearer " + _authToken);
			}

			return await SendRequestAsync(request);
		}

		private static async AlterTask<IHttpTransportClientResponse> SendRequestAsync(UnityWebRequest request)
		{
			request.downloadHandler = new DownloadHandlerBuffer();
			await request.SendWebRequest().ToUniTask();
			return new UnityHttpTransportClientResponse(request);
		}
	}
}
