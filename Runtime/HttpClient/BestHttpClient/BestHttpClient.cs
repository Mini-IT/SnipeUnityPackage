#if BEST_HTTP

#if BEST_HTTP_TLS && (!UNITY_WEBGL || UNITY_EDITOR)
#define TLS_SUPPORTED
#endif

using System;
using System.IO;
using Best.HTTP;
using Best.HTTP.Request.Authenticators;
using Best.HTTP.Request.Upload.Forms;
using Cysharp.Threading.Tasks;

#if TLS_SUPPORTED
using Best.TLSSecurity;
#endif

namespace MiniIT.Http
{
	public class BestHttpClient : IHttpClient
	{
		private readonly TimeSpan _defaultConnectTimeout = TimeSpan.FromSeconds(4);

		private string _authToken;

#if TLS_SUPPORTED
		public BestHttpClient()
		{
			TLSSecurity.Setup();
		}
#endif

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
			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
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

			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
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

			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}
	}
}

#endif
