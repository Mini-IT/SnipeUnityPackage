#if BEST_HTTP

#if BEST_HTTP_TLS && (!UNITY_WEBGL || UNITY_EDITOR)
#define TLS_SUPPORTED
#endif

using System;
using System.IO;
using Best.HTTP;
using Best.HTTP.Request.Authenticators;
using Best.HTTP.Request.Upload.Forms;
using Best.HTTP.Shared;
using Cysharp.Threading.Tasks;

#if TLS_SUPPORTED
using Best.TLSSecurity;
#endif

namespace MiniIT.Http
{
	public class BestHttpClient : IHttpClient
	{
		private static bool s_tlsInitialized = false;

		private readonly TimeSpan _defaultConnectTimeout = TimeSpan.FromSeconds(3);

		private string _authToken;
		private string _persistentClientId;

#if TLS_SUPPORTED
		public BestHttpClient()
		{
			if (s_tlsInitialized)
			{
				return;
			}

			s_tlsInitialized = true;

			// Disable OSCP cache for performance
			SecurityOptions.OCSP.OCSPCache.DatabaseOptions.DiskManager.MaxCacheSizeInBytes = 0;

			TLSSecurity.Setup();
			HTTPManager.LocalCache = null;
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

		public void SetPersistentClientId(string id)
		{
			_persistentClientId = id;
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri)
		{
			var request = HTTPRequest.CreateGet(uri);
			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
			request.DownloadSettings.DisableCache = true;
			request.SetHeader("Cache-Control", "no-cache");
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout)
		{
			var request = HTTPRequest.CreateGet(uri);
			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
			request.TimeoutSettings.Timeout = timeout;
			request.DownloadSettings.DisableCache = true;
			request.SetHeader("Cache-Control", "no-cache");
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string json, TimeSpan timeout)
		{
			var request = HTTPRequest.CreatePost(uri);
			request.SetHeader("Content-Type", "application/json; charset=UTF-8");

			FillHeaders(request);

			var data = System.Text.Encoding.UTF8.GetBytes(json);
			request.UploadSettings.UploadStream = new MemoryStream(data);

			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
			request.TimeoutSettings.Timeout = timeout;
			request.DownloadSettings.DisableCache = true;
			request.SetHeader("Cache-Control", "no-cache");
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout)
		{
			var request = HTTPRequest.CreatePost(uri);

			FillHeaders(request);

			request.UploadSettings.UploadStream = new MultipartFormDataStream()
				.AddField(name, content);

			request.TimeoutSettings.ConnectTimeout = _defaultConnectTimeout;
			request.TimeoutSettings.Timeout = timeout;
			request.DownloadSettings.DisableCache = true;
			request.SetHeader("Cache-Control", "no-cache");
			await request.Send();
			return new BestHttpClientResponse(request.Response);
		}

		private void FillHeaders(HTTPRequest request)
		{
			if (!string.IsNullOrEmpty(_authToken))
			{
				request.Authenticator = new BearerTokenAuthenticator(_authToken);
			}

			if (!string.IsNullOrEmpty(_persistentClientId))
			{
				request.SetHeader("DeviceID", _persistentClientId);
			}
		}
	}
}

#endif
