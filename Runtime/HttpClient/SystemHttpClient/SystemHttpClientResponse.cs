using System.Net.Http;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public struct SystemHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode { get; }
		public readonly bool IsSuccess => _response.IsSuccessStatusCode;
		public string Error { get; }

		private readonly HttpResponseMessage _response;

		public SystemHttpClientResponse(HttpResponseMessage response) : this()
		{
			_response = response;
			ResponseCode = (long)response.StatusCode;
			Error = $"{(int)response.StatusCode} {response.StatusCode}";
		}

		public async UniTask<string> GetContentAsync()
		{
			return await _response.Content.ReadAsStringAsync();
		}

		public void Dispose()
		{
			_response?.Dispose();
		}
	}
}
