using System.Net;
using System.Net.Http;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public readonly struct SystemHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode { get; }
		public bool IsSuccess { get; }
		public string Error { get; }

		private readonly HttpResponseMessage _response;

		public SystemHttpClientResponse(HttpResponseMessage response) : this()
		{
			_response = response;

			if (response != null)
			{
				IsSuccess = response.IsSuccessStatusCode;
				ResponseCode = (long)response.StatusCode;
				Error = response.IsSuccessStatusCode ? null : $"{(int)response.StatusCode} {response.StatusCode}";
			}
			else
			{
				IsSuccess = false;
				var statusCode = HttpStatusCode.RequestTimeout;
				ResponseCode = (long)statusCode;
				Error = $"{(int)statusCode} {statusCode}";
			}
		}

		public SystemHttpClientResponse(HttpStatusCode responseCode, string error)
		{
			_response = null;
			ResponseCode = (long)responseCode;
			IsSuccess = ResponseCode >= 200 && ResponseCode < 300;
			Error = error;
		}

		public async UniTask<string> GetStringContentAsync()
		{
			if (_response?.Content == null)
			{
				return null;
			}

			return await _response.Content.ReadAsStringAsync();
		}

		public async UniTask<byte[]> GetBinaryContentAsync()
		{
			if (_response?.Content == null)
			{
				return null;
			}

			return await _response.Content.ReadAsByteArrayAsync();
		}

		public void Dispose()
		{
			_response?.Dispose();
		}
	}
}
