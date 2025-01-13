
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public class SystemHttpClient : IHttpClient, IDisposable
	{
		private readonly HttpClient _httpClient;

		public SystemHttpClient()
		{
			_httpClient = new HttpClient();
		}

		public void Reset()
		{
			_httpClient.DefaultRequestHeaders.Clear();
		}

		public void SetAuthToken(string token)
		{
			_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri)
		{
			HttpResponseMessage response = await _httpClient.GetAsync(uri);
			return new SystemHttpClientResponse(response);
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string content)
		{
			var requestContent = new StringContent(content, Encoding.UTF8, "application/json");

			var response = await _httpClient.PostAsync(uri, requestContent);
			return new SystemHttpClientResponse(response);
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content)
		{
			var requestContent = new MultipartFormDataContent();
			requestContent.Add(new ByteArrayContent(content), name);

			var response = await _httpClient.PostAsync(uri, requestContent);
			return new SystemHttpClientResponse(response);
		}

		public void Dispose()
		{
			_httpClient.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
