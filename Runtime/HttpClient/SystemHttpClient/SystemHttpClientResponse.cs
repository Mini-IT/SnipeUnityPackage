using System.Net.Http;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public struct SystemHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode { get; }
		public bool IsSuccess { get; }
		public string Error { get; }

		private readonly HttpResponseMessage _response;

		public SystemHttpClientResponse(HttpResponseMessage response) : this()
		{
			_response = response;
			ResponseCode = response != null ? (long)response.StatusCode : 0;
			IsSuccess = _response?.IsSuccessStatusCode ?? false;

			if (response != null)
			{
				Error = IsSuccess ? null : $"{(int)response.StatusCode} {response.StatusCode}";
			}
			else
			{
				Error = "No response";
			}
		}

		public SystemHttpClientResponse(long responseCode, bool isSuccess, string error)
		{
			_response = null;
			ResponseCode = responseCode;
			IsSuccess = isSuccess;
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
