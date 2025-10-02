
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public class SystemHttpClient : IHttpClient, IDisposable
	{
		private readonly HttpClient _httpClient;

		public SystemHttpClient()
		{
			_httpClient = new HttpClient();
			_httpClient.Timeout = TimeSpan.FromSeconds(4);
		}

		public void Reset()
		{
			_httpClient.DefaultRequestHeaders.Clear();
		}

		public void SetAuthToken(string token)
		{
			_httpClient.DefaultRequestHeaders.Authorization = !string.IsNullOrEmpty(token)
				? new AuthenticationHeaderValue("Bearer", token)
				: null;
		}

		public void SetPersistentClientId(string id)
		{
			_httpClient.DefaultRequestHeaders.Remove("DeviceID");

			if (!string.IsNullOrEmpty(id))
			{
				_httpClient.DefaultRequestHeaders.Add("DeviceID", id);
			}
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri)
		{
			SystemHttpClientResponse result;

			try
			{
				HttpResponseMessage response = await _httpClient.GetAsync(uri);
				result = new SystemHttpClientResponse(response);
			}
			catch (Exception e)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.BadRequest, e.Message);
			}

			return result;
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout)
		{
			SystemHttpClientResponse result;

			using var cts = new CancellationTokenSource(timeout);

			try
			{
				HttpResponseMessage response = await _httpClient.GetAsync(uri, cts.Token);
				result = new SystemHttpClientResponse(response);
			}
			catch (OperationCanceledException)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.RequestTimeout, "RequestTimeout");
			}
			catch (Exception e)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.BadRequest, e.Message);
			}

			return result;
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout)
		{
			using var requestContent = new StringContent(content, Encoding.UTF8, "application/json");

			SystemHttpClientResponse result;

			using var cts = new CancellationTokenSource(timeout);

			try
			{
				HttpResponseMessage response = await _httpClient.PostAsync(uri, requestContent, cts.Token);
				result = new SystemHttpClientResponse(response);
			}
			catch (OperationCanceledException)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.RequestTimeout, "RequestTimeout");
			}
			catch (Exception e)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.BadRequest, e.Message);
			}

			return result;
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout)
		{
			using var requestContent = new MultipartFormDataContent();
			requestContent.Add(new ByteArrayContent(content), name);

			SystemHttpClientResponse result;

			using var cts = new CancellationTokenSource(timeout);

			try
			{
				HttpResponseMessage response = await _httpClient.PostAsync(uri, requestContent, cts.Token);
				result = new SystemHttpClientResponse(response);
			}
			catch (OperationCanceledException)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.RequestTimeout, "RequestTimeout");
			}
			catch (Exception e)
			{
				result = new SystemHttpClientResponse(HttpStatusCode.BadRequest, e.Message);
			}

			return result;
		}

		public void Dispose()
		{
			_httpClient.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
