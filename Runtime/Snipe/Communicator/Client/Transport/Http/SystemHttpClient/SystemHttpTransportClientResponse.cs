using System.Net.Http;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	public struct SystemHttpTransportClientResponse : IHttpTransportClientResponse
	{
		public long ResponseCode { get; }
		public readonly bool IsSuccess => _response.IsSuccessStatusCode;
		public string Error { get; }

		private readonly HttpResponseMessage _response;

		public SystemHttpTransportClientResponse(HttpResponseMessage response) : this()
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
