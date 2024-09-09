
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using MiniIT.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	public class SystemHttpClient : IHttpTransportClient, IDisposable
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

		public async AlterTask<IHttpTransportClientResponse> GetAsync(Uri uri)
		{
			HttpResponseMessage response = await _httpClient.GetAsync(uri);
			return new SystemHttpTransportClientResponse(response);
		}

		public async AlterTask<IHttpTransportClientResponse> PostJsonAsync(Uri uri, string content)
		{
			var requestContent = new StringContent(content, Encoding.UTF8, "application/json");

			var response = await _httpClient.PostAsync(uri, requestContent);
			return new SystemHttpTransportClientResponse(response);
		}

		public void Dispose()
		{
			_httpClient.Dispose();
		}
	}
}
