#if BEST_HTTP

using Cysharp.Threading.Tasks;
using Best.HTTP;

namespace MiniIT.Http
{
	public readonly struct BestHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode => _response.StatusCode;
		public bool IsSuccess => _response.IsSuccess;
		public string Error => _response.Message;

		private readonly HTTPResponse _response;

		public BestHttpClientResponse(HTTPResponse response) : this()
		{
			_response = response;
		}

		public UniTask<string> GetContentAsync()
		{
			string content = _response.DataAsText;
			return UniTask.FromResult(content);
		}

		public void Dispose()
		{
			_response?.Dispose();
		}
	}
}

#endif
