using System.Net.Http;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public struct SystemHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode { get; }
		public readonly bool IsSuccess => _response != null && _response.IsSuccessStatusCode;
		public string Error { get; }

		private readonly HttpResponseMessage _response;

		public SystemHttpClientResponse(HttpResponseMessage response) : this()
		{
			_response = response;

			if (response != null)
			{
				ResponseCode = (long)response.StatusCode;
				Error = response.IsSuccessStatusCode ? null : $"{(int)response.StatusCode} {response.StatusCode}";
			}
			else
			{
				var statusCode = System.Net.HttpStatusCode.RequestTimeout;
				ResponseCode = (long)statusCode;
				Error = $"{(int)statusCode} {statusCode}";
			}
		}

		public async UniTask<string> GetStringContentAsync()
		{
			return await _response.Content.ReadAsStringAsync();
		}

		public async UniTask<byte[]> GetBinaryContentAsync()
		{
			return await _response.Content.ReadAsByteArrayAsync();
		}

		public void Dispose()
		{
			_response?.Dispose();
		}
	}
}
