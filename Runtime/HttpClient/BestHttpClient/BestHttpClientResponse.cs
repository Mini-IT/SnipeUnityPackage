#if BEST_HTTP

using Cysharp.Threading.Tasks;
using Best.HTTP;
using Best.HTTP.Response;

namespace MiniIT.Http
{
	public readonly struct BestHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode => _response?.StatusCode ?? HTTPStatusCodes.BadRequest;
		public bool IsSuccess => _response?.IsSuccess ?? false;
		public string Error => _response?.Message ?? string.Empty;

		private readonly HTTPResponse _response;

		public BestHttpClientResponse(HTTPResponse response) : this()
		{
			_response = response;
		}

		public UniTask<string> GetStringContentAsync()
		{
			string content = _response.DataAsText;
			return UniTask.FromResult(content);
		}

		public UniTask<byte[]> GetBinaryContentAsync()
		{
			byte[] content = _response.Data;
			return UniTask.FromResult(content);
		}

		public void Dispose()
		{
			_response?.Dispose();
		}
	}
}

#endif
