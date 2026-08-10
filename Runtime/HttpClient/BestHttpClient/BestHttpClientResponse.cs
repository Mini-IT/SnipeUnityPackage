#if BEST_HTTP

using Cysharp.Threading.Tasks;
using Best.HTTP;
using Best.HTTP.Response;

namespace MiniIT.Http
{
	public readonly struct BestHttpClientResponse : IHttpClientResponse
	{
		private const long REQUEST_TIMEOUT_RESPONSE_CODE = 408;
		private const string REQUEST_TIMEOUT_ERROR = "408 RequestTimeout";

		public long ResponseCode => _hasSyntheticResponse ? _responseCode : _response?.StatusCode ?? HTTPStatusCodes.BadRequest;
		public bool IsSuccess => _hasSyntheticResponse ? _isSuccess : _response?.IsSuccess ?? false;
		public string Error => _hasSyntheticResponse ? _error : _response?.Message ?? string.Empty;

		private readonly HTTPResponse _response;
		private readonly long _responseCode;
		private readonly bool _isSuccess;
		private readonly string _error;
		private readonly bool _hasSyntheticResponse;

		public BestHttpClientResponse(HTTPResponse response) : this()
		{
			_response = response;
		}

		private BestHttpClientResponse(long responseCode, bool isSuccess, string error) : this()
		{
			_responseCode = responseCode;
			_isSuccess = isSuccess;
			_error = error;
			_hasSyntheticResponse = true;
		}

		public static BestHttpClientResponse CreateTimeout()
		{
			return new BestHttpClientResponse(REQUEST_TIMEOUT_RESPONSE_CODE, false, REQUEST_TIMEOUT_ERROR);
		}

		public UniTask<string> GetStringContentAsync()
		{
			if (_response == null)
			{
				return UniTask.FromResult<string>(null);
			}

			string content = _response.DataAsText;
			return UniTask.FromResult(content);
		}

		public UniTask<byte[]> GetBinaryContentAsync()
		{
			if (_response == null)
			{
				return UniTask.FromResult<byte[]>(null);
			}

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
