
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
		private const int DEFAULT_TIMEOUT_SECONDS = 15;

		private readonly HttpClient _httpClient;

		public SystemHttpClient()
		{
			var handler = new HttpClientHandler
			{
				AutomaticDecompression =
					DecompressionMethods.GZip |
					DecompressionMethods.Deflate
			};

			_httpClient = new HttpClient(handler, disposeHandler: true);
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

		public UniTask<IHttpClientResponse> Get(Uri uri)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			return Send(request, TimeSpan.FromSeconds(DEFAULT_TIMEOUT_SECONDS));
		}

		public UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, uri);
			return Send(request, timeout);
		}

		public UniTask<IHttpClientResponse> PostJson(Uri uri, string json, TimeSpan timeout)
		{
			var request = new HttpRequestMessage(HttpMethod.Post, uri)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			};

			return Send(request, timeout);
		}

		public UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout)
		{
			var form = new MultipartFormDataContent();
			form.Add(new ByteArrayContent(content), name);

			var request = new HttpRequestMessage(HttpMethod.Post, uri)
			{
				Content = form
			};

			return Send(request, timeout);
		}

		private async UniTask<IHttpClientResponse> Send(HttpRequestMessage request, TimeSpan timeout)
		{
			await UniTask.SwitchToThreadPool();

			try
			{
				using var cts = new CancellationTokenSource(timeout);

				var response = await _httpClient
					.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
					.ConfigureAwait(false);

				return new SystemHttpClientResponse(response);
			}
			catch (OperationCanceledException)
			{
				return new SystemHttpClientResponse(HttpStatusCode.RequestTimeout, "RequestTimeout");
			}
			catch (Exception e)
			{
				return new SystemHttpClientResponse(HttpStatusCode.BadRequest, e.Message);
			}
			finally
			{
				await UniTask.SwitchToMainThread();
			}
		}

		public void Dispose()
		{
			_httpClient.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
