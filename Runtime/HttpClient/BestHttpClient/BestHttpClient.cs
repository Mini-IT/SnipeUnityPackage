#if BEST_HTTP

using System;
using System.IO;
using Best.HTTP;
using Best.HTTP.Request.Authenticators;
using Best.HTTP.Request.Upload.Forms;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public class BestHttpClient : IHttpClient
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

		public async UniTask<IHttpClientResponse> Get(Uri uri)
		{
			var request = HTTPRequest.CreateGet(uri);
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout)
		{
			var request = HTTPRequest.CreateGet(uri);
			request.TimeoutSettings.Timeout = timeout;
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string json)
		{
			var request = HTTPRequest.CreatePost(uri);
			request.SetHeader("Content-Type", "application/json; charset=UTF-8");

			if (!string.IsNullOrEmpty(_authToken))
			{
				request.Authenticator = new BearerTokenAuthenticator(_authToken);
			}

			var data = System.Text.Encoding.UTF8.GetBytes(json);
			request.UploadSettings.UploadStream = new MemoryStream(data);

			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content)
		{
			var request = HTTPRequest.CreatePost(uri);

			if (!string.IsNullOrEmpty(_authToken))
			{
				request.Authenticator = new BearerTokenAuthenticator(_authToken);
			}

			request.UploadSettings.UploadStream = new MultipartFormDataStream()
				.AddField(name, content);

			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}
	}
}

#endif
